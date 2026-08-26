using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

// E2E phase gate for XafReportScheduler.
// Run with:  dotnet run --project XafReportScheduler.E2ETests
//
// Assertions:
//  1. Editable seed: "Orders Report" is a plain (non-predefined) ReportDataV2 and its
//     end-user Report Designer opens from the ListView.
//  2. Criteria + run: the disabled "E2E Acme Orders (CSV)" schedule, triggered via
//     "Run Now", exports a CSV containing ORD-001 but not ORD-002/ORD-003, and the
//     schedule's detail view reflects LastRunStatus = Succeeded.
//  3. Scheduling registered: ReportScheduleSyncService registered the one enabled
//     recurring job on startup.

const string BaseUrl = "http://localhost:5100";
const string OrdersReportName = "Orders Report";
const string ScheduleName = "E2E Acme Orders (CSV)";
const string ConnectionString =
    @"Data Source=(localdb)\mssqllocaldb;Integrated Security=SSPI;Initial Catalog=XafReportScheduler;Encrypt=False";

var repoRoot = FindRepoRoot();
var blazorProj = Path.Combine(repoRoot, "XafReportScheduler.Blazor.Server");
var outputDir = Path.Combine(blazorProj, "output");
// ponytail: write to a scratch dir, not docs/screenshots -- the committed PNGs there are
// evidence from a real run and shouldn't be silently overwritten by every local E2E pass.
var screenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
Directory.CreateDirectory(screenshotDir);
Console.WriteLine($"Screenshots: {screenshotDir}");

Process? app = null;
IPage? page = null;
IPlaywright? playwright = null;
IBrowser? browser = null;
var appOutput = new System.Text.StringBuilder();
var failed = false;
var missingBrowser = false;

try
{
    Step("Pre-clean: remove Orders/Customers so the Updater re-seeds fresh relative dates, and old CSVs");
    try { Sql("DELETE FROM Orders; DELETE FROM Customers;"); }
    catch (SqlException ex) when (ex.Number is 4060 or 208)
    {
        Console.WriteLine("    (no database yet -- skipping pre-clean)");
    }
    if (Directory.Exists(outputDir))
        foreach (var f in Directory.GetFiles(outputDir, "*.csv")) File.Delete(f);

    Step("Build Blazor.Server");
    RunOrThrow("dotnet", $"build \"{blazorProj}\" -v q --nologo");

    Step("Start Blazor app on :5100");
    app = StartApp(blazorProj, appOutput);
    await WaitForHttpOk(appOutput);

    try
    {
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    }
    catch (Exception ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
    {
        missingBrowser = true;
        Console.WriteLine("\nPlaywright's Chromium browser is not installed. Run:");
        Console.WriteLine("    pwsh XafReportScheduler.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium");
        throw;
    }
    page = await NewPage(browser);

    Step("Log in as Admin");
    await Login(page);

    // ---------- Assertion 1: editable seed ----------
    {
        Step("SQL: Orders Report has no PredefinedReportTypeName (it is a plain, editable report)");
        var predefinedType = SqlScalar(
            $"SELECT PredefinedReportTypeName FROM ReportDataV2 WHERE DisplayName = '{OrdersReportName}'");
        Assert(string.IsNullOrEmpty(predefinedType), $"PredefinedReportTypeName is null/empty (was: {predefinedType ?? "<null>"})");

        Step("Open Orders Report and launch the end-user Report Designer");
        await page.GotoAsync($"{BaseUrl}/ReportDataV2_ListView", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var reportRow = page.GetByRole(AriaRole.Row)
            .Filter(new() { Has = page.GetByRole(AriaRole.Gridcell, new() { Name = OrdersReportName, Exact = true }) });
        await reportRow.GetByRole(AriaRole.Checkbox).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Report Designer", Exact = true }).ClickAsync();

        var designer = page.Locator(".dxrd-designer, dx-report-designer");
        await designer.First.WaitForAsync(new() { Timeout = 60_000 });
        Assert(await designer.CountAsync() > 0, "report designer surface (.dxrd-designer / dx-report-designer) rendered");
        await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-designer.png") });

        // Navigate away from the designer tab before continuing.
        await page.GotoAsync($"{BaseUrl}/ReportSchedule_ListView", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    // ---------- Assertion 2: criteria-filtered CSV via Run Now ----------
    {
        Step("Open the E2E Acme Orders (CSV) schedule and click Run Now");
        await page.GotoAsync($"{BaseUrl}/ReportSchedule_ListView", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByText(ScheduleName, new() { Exact = true }).First.DblClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Run Now", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes", Exact = true }).ClickAsync();

        Step("Poll output/ for the exported CSV (up to 30s)");
        var csvPath = await PollForCsv(outputDir, ScheduleName, TimeSpan.FromSeconds(30));
        Assert(csvPath is not null, $"CSV file matching '{ScheduleName}_*.csv' appeared in {outputDir}");
        var csv = File.ReadAllText(csvPath!);
        Console.WriteLine($"    exported CSV:\n{csv.Trim().ReplaceLineEndings("\n    ")}");
        Assert(csv.Contains("ORD-001"), "ORD-001 (Acme Corp, within last week) present");
        Assert(!csv.Contains("ORD-002"), "ORD-002 (Acme Corp, 30 days old) excluded by date criterion");
        Assert(!csv.Contains("ORD-003"), "ORD-003 (Globex) excluded by Customer.Name criterion");

        Step("Reload the schedule detail view and assert LastRunStatus = Succeeded");
        // Blazor Server reload: DOMContentLoaded fires on the static shell before the SignalR
        // circuit reconnects and re-renders the view -- poll the rendered input values instead
        // of reading them once.
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        string[] inputValues = [];
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            inputValues = await page.Locator("input, textarea")
                .EvaluateAllAsync<string[]>("els => els.map(e => (e.name || e.id || '') + '=' + e.value)");
            if (inputValues.Any(v => v.Contains("Succeeded"))) break;
            await Task.Delay(1000);
        }
        Console.WriteLine("    input values: " + string.Join(" | ", inputValues));
        Assert(inputValues.Any(v => v.Contains("Succeeded")), "'Succeeded' found among detail view input values");
        await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, "e2e-schedule-run.png") });
    }

    // ---------- Assertion 3: scheduling registered ----------
    {
        Step("App stdout must report the enabled recurring job was registered");
        var registered = await PollForLog(appOutput, "Registered 1 report schedules", TimeSpan.FromSeconds(30));
        Assert(registered, "app stdout contains 'Registered 1 report schedules'");
    }

    Console.WriteLine("\n=== E2E PASSED ===");
}
catch (Exception ex)
{
    failed = true;
    Console.WriteLine($"\n=== E2E FAILED ===\n{ex}");
    Console.WriteLine("\n--- app stdout/stderr (last 60 lines) ---");
    Console.WriteLine(string.Join('\n', appOutput.ToString().Split('\n').TakeLast(60)));
    if (page is not null)
    {
        Console.WriteLine($"\n--- page URL at failure: {page.Url}");
        try
        {
            var bodyText = (await page.InnerTextAsync("body")).Trim().Replace("\n", " ");
            Console.WriteLine($"--- body text (first 800 chars): {bodyText[..Math.Min(800, bodyText.Length)]}");
        }
        catch (Exception diagEx) { Console.WriteLine($"--- body text read failed: {diagEx.Message}"); }
        try
        {
            var debugPath = Path.Combine(Path.GetTempPath(), $"e2e-failure-{DateTime.Now:HHmmss}.png");
            await page.ScreenshotAsync(new() { Path = debugPath });
            Console.WriteLine($"--- debug screenshot: {debugPath}");
        }
        catch (Exception diagEx) { Console.WriteLine($"--- screenshot failed: {diagEx.Message}"); }
    }
}
finally
{
    Step("Stop app, free port 5100");
    if (browser is not null) { try { await browser.CloseAsync(); } catch { /* best-effort */ } }
    playwright?.Dispose();
    KillApp(ref app);
}
return missingBrowser ? 2 : (failed ? 1 : 0);

// ---------- helpers ----------

static void Step(string name) => Console.WriteLine($"\n--- {name}");

static void Assert(bool condition, string what)
{
    if (!condition) throw new Exception($"Assert failed: {what}");
    Console.WriteLine($"    [ok] {what}");
}

static async Task Login(IPage page)
{
    // Blazor Server circuit-connect race: DOMContentLoaded fires on the static shell before
    // the SignalR circuit attaches event handlers, so a Fill() right after that point can be
    // silently dropped server-side ("user name must not be empty"). Wait for NetworkIdle
    // (circuit connected) and verify the value actually bound before submitting.
    await page.GotoAsync($"{BaseUrl}/LoginPage", new() { WaitUntil = WaitUntilState.NetworkIdle });
    var userField = page.Locator("input[type='text'], input[name*='sername']").First;
    await userField.WaitForAsync(new() { Timeout = 20_000 });
    for (var i = 0; i < 10; i++)
    {
        await userField.FillAsync("Admin");
        if (await userField.InputValueAsync() == "Admin") break;
        await Task.Delay(300);
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Log In" }).ClickAsync();
    // XAF Blazor navigates away from /LoginPage on success; wait for that rather than a fixed delay.
    await page.WaitForURLAsync(url => !url.Contains("LoginPage", StringComparison.OrdinalIgnoreCase),
        new() { Timeout = 20_000 });
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 20_000 });
}

static async Task<string?> PollForCsv(string outputDir, string scheduleName, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (Directory.Exists(outputDir))
        {
            var match = Directory.GetFiles(outputDir, $"{scheduleName}_*.csv").FirstOrDefault();
            if (match is not null) return match;
        }
        await Task.Delay(1000);
    }
    return null;
}

static async Task<bool> PollForLog(System.Text.StringBuilder buffer, string needle, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        lock (buffer)
        {
            if (buffer.ToString().Contains(needle)) return true;
        }
        await Task.Delay(1000);
    }
    return false;
}

static async Task<IPage> NewPage(IBrowser browser)
{
    var page = await browser.NewPageAsync();
    page.SetDefaultTimeout(30_000);
    page.SetDefaultNavigationTimeout(30_000);
    return page;
}

static Process StartApp(string blazorProj, System.Text.StringBuilder appOutput)
{
    // launchSettings.json's applicationUrl (5000/5001) overrides ASPNETCORE_URLS unless
    // --no-launch-profile is passed; --urls on the command line wins over both. Belt and
    // braces: pass --urls AND keep the env var.
    var psi = new ProcessStartInfo("dotnet",
        $"run --no-build --no-launch-profile --project \"{blazorProj}\" --urls {BaseUrl}")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        EnvironmentVariables = { ["ASPNETCORE_URLS"] = BaseUrl, ["ASPNETCORE_ENVIRONMENT"] = "Development" },
    };
    var p = Process.Start(psi)!;
    p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (appOutput) appOutput.AppendLine(e.Data); };
    p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (appOutput) appOutput.AppendLine(e.Data); };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    return p;
}

static void KillApp(ref Process? app)
{
    if (app is null) return;
    try { app.Kill(entireProcessTree: true); app.WaitForExit(10_000); } catch { /* already gone */ }
    app.Dispose();
    app = null;
}

static async Task WaitForHttpOk(System.Text.StringBuilder appOutput)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    for (var i = 0; i < 90; i++)
    {
        try
        {
            var resp = await http.GetAsync(BaseUrl);
            if (resp.IsSuccessStatusCode) { Console.WriteLine($"    app ready at {BaseUrl}"); return; }
        }
        catch { /* not up yet */ }
        await Task.Delay(2000);
    }
    string tail;
    lock (appOutput) tail = string.Join('\n', appOutput.ToString().Split('\n').TakeLast(40));
    throw new Exception($"app did not become ready at {BaseUrl}\n--- app stdout/stderr (last 40 lines) ---\n{tail}");
}

static void RunOrThrow(string file, string args)
{
    var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })!;
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"`{file} {args}` exited with {p.ExitCode}");
}

static int Sql(string sql)
{
    using var conn = new SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new SqlCommand(sql, conn);
    return cmd.ExecuteNonQuery();
}

static string? SqlScalar(string sql)
{
    using var conn = new SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new SqlCommand(sql, conn);
    return cmd.ExecuteScalar()?.ToString();
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (; dir is not null; dir = dir.Parent)
        if (dir.GetFiles("*.sln").Length > 0) return dir.FullName;
    throw new Exception("repo root (.sln) not found above " + AppContext.BaseDirectory);
}
