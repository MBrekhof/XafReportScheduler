# XafReportScheduler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An XAF Blazor POC that (1) seeds code-authored reports as *user-editable* (non-predefined) `ReportDataV2` rows, (2) lets an admin attach criteria such as "last 7 days" / "name like Acme*" to a report via XAF's criteria editor, and (3) runs the report on a cron schedule (e.g. every Saturday 07:00) or on demand, exporting to a folder.

**Architecture:** Standard `dotnet new dx.xaf` solution (Module + Blazor.Server). The Module owns the sample domain (`Customer`/`Order`), a code-built `OrdersReport` XtraReport, a one-shot seeding updater that writes the report through `IReportStorage.SaveReport` (never `PredefinedReportsUpdater`, so `IsPredefined == false`), the `ReportSchedule` entity, and `ReportJob` (logon as service user → `IReportExportService.LoadReport/SetupReport` → export). Blazor.Server hosts Hangfire (in-memory storage), a startup sync that turns enabled `ReportSchedule` rows into Hangfire recurring jobs, a controller that re-syncs on save and offers **Run Now**. An E2E console (C# Playwright) is the phase gate.

**Tech Stack:** .NET 10, DevExpress XAF 26.1.4 (EF Core, Blazor Server, ReportsV2, Security/Password), SQL Server LocalDB, Hangfire.AspNetCore + Hangfire.InMemory, Microsoft.Playwright (C#).

**Spec:** the conversation of 2026-08-23 (this file is the spec of record — see "Decisions" below).

## Global Constraints

- DevExpress packages pinned to **26.1.4** (local feed `DevExpress 26.1 Local`); never mix 25.2.
- **EF Core only** — never XPO (owner rule).
- All DX behaviour claims must be verified in dxdocs or the installed source at `C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp` — do not guess APIs.
- The seeded report MUST NOT be registered through `PredefinedReportsUpdater`. Its `ReportDataV2.PredefinedReportTypeName` must stay null/empty.
- Module project must not reference Hangfire. Hangfire lives in Blazor.Server only.
- Ponytail mode: shortest working diff; mark deliberate ceilings with `// ponytail:` comments.
- Commit locally after each task. **Never push / never create the GitHub repo** — owner gives that go separately.
- Database: LocalDB `(localdb)\mssqllocaldb`, catalog `XafReportScheduler`. Dev app port 5000; E2E runs its own instance on port 5100.
- Output folder for exports: `ReportJobs:OutputFolder` config, default `output` (relative to content root), git-ignored.

## Decisions (why)

- **Non-predefined seeding** — dxdocs: `IsPredefined` is true only for rows registered via `PredefinedReportsUpdater`; runtime rows are editable in the end-user designer. We seed once (guard by `DisplayName`) and never overwrite, so user edits survive restarts.
- **Criteria string, not `ReportParametersObjectBase`** — for scheduled runs there is no dialog; a criteria string is the natural serialized form and XAF's `[CriteriaOptions]` editor gives the UI for free. Typed parameter dialogs remain the domain of the sibling repo XafReportParametersObjects.
- **Hangfire.InMemory** — schedules are re-registered from `ReportSchedule` rows on every startup, so nothing is lost except runs missed while the app is down. Persistent storage is an upgrade, not a requirement, for a POC.
- **Folder sink only** — e-mail delivery skipped (YAGNI for the POC; add an SMTP sink when somebody needs it).
- **No run-history entity** — last-run fields on `ReportSchedule` are enough for the POC.

## File Structure

```
XafReportScheduler/
  XafReportScheduler.slnx
  .gitignore
  docs/plans/2026-08-23-xaf-report-scheduler.md      (this file)
  XafReportScheduler.Module/
    BusinessObjects/Customer.cs                      sample domain
    BusinessObjects/Order.cs
    BusinessObjects/ReportSchedule.cs                schedule entity + criteria editor + ExportFormat enum
    BusinessObjects/XafReportSchedulerEFCoreDbContext.cs   (template; add DbSets)
    Reports/OrdersReport.cs                          code-built XtraReport over Order
    DatabaseUpdate/Updater.cs                        seed customers/orders/admin (template) + call ReportSeeder
    DatabaseUpdate/ReportSeeder.cs                   seeds editable ReportDataV2 rows + ReportSchedule rows
    Services/ReportJob.cs                            logon → load → criteria → export → record result
    Module.cs                                        (template; registers ReportsModuleV2, updaters)
  XafReportScheduler.Blazor.Server/
    Startup.cs                                       (template; + Hangfire, + ReportJob, + sync service)
    Services/ReportScheduleJobs.cs                   static helpers: Register/Remove recurring job, Enqueue run-now
    Services/ReportScheduleSyncService.cs            IHostedService: rows → recurring jobs at startup
    Controllers/ReportScheduleController.cs          Run Now action + re-sync on commit
    appsettings.json                                 + ReportJobs section
  XafReportScheduler.E2ETests/
    XafReportScheduler.E2ETests.csproj               console, Microsoft.Playwright + Microsoft.Data.SqlClient
    Program.cs                                       phase-gate script
  README.md, CLAUDE.md, SESSION_HANDOFF.md
```

---

### Task 1: Scaffold the solution

**Files:**
- Create: everything under `C:\Projects\XafReportScheduler\` via `dotnet new dx.xaf`
- Create: `.gitignore`, `output/` ignored
- Modify: `XafReportScheduler.Blazor.Server/XafReportScheduler.Blazor.Server.csproj` (add Hangfire packages)

**Interfaces:**
- Produces: solution `XafReportScheduler.slnx`; projects `XafReportScheduler.Module`, `XafReportScheduler.Blazor.Server`; DbContext class name `XafReportSchedulerEFCoreDbContext` (check the generated name and use it verbatim in later tasks); connection string name `ConnectionString` in `appsettings.json`.

- [ ] **Step 1: Generate**

```bash
cd /c/Projects/XafReportScheduler
dotnet new dx.xaf -n XafReportScheduler -o . -orm EFCore -db SqlServer -dbu Auto -p Blazor -s Password -m Reports Validation Appearance
dotnet new gitignore
printf 'output/\n*.user\n.vs/\n' >> .gitignore
```

If `dotnet new` refuses because the directory is not empty (the `docs/` folder exists), pass `--force`.

- [ ] **Step 2: Pin LocalDB + output folder in appsettings**

`XafReportScheduler.Blazor.Server/appsettings.json` — ensure:

```json
"ConnectionStrings": {
  "ConnectionString": "Integrated Security=SSPI;Pooling=false;Data Source=(localdb)\\mssqllocaldb;Initial Catalog=XafReportScheduler;Encrypt=False"
},
"ReportJobs": {
  "UserName": "Admin",
  "Password": "",
  "OutputFolder": "output"
}
```

- [ ] **Step 3: Add Hangfire to Blazor.Server only**

```bash
dotnet add XafReportScheduler.Blazor.Server package Hangfire.AspNetCore
dotnet add XafReportScheduler.Blazor.Server package Hangfire.InMemory
```

- [ ] **Step 4: Build**

Run: `dotnet build XafReportScheduler.slnx -v q --nologo`
Expected: 0 errors. Record the exact DbContext class name and `DevExpress.*` package version (must be 26.1.4) in the task report.

- [ ] **Step 5: Smoke-run**

```bash
netstat -ano | grep :5000    # must be free
dotnet run --project XafReportScheduler.Blazor.Server --no-build &
curl -s -o /dev/null -w "%{http_code}" --max-time 60 http://localhost:5000/
```
Expected: 200. Then kill the process you started (`taskkill //PID <pid> //F //T`) and verify the port is free.

- [ ] **Step 6: Commit**

```bash
git init
git add -A
git commit -m "chore: scaffold XAF 26.1 Blazor solution (EF Core, LocalDB, ReportsV2) + Hangfire packages"
```

---

### Task 2: Sample domain + editable seeded report

**Files:**
- Create: `XafReportScheduler.Module/BusinessObjects/Customer.cs`
- Create: `XafReportScheduler.Module/BusinessObjects/Order.cs`
- Create: `XafReportScheduler.Module/Reports/OrdersReport.cs`
- Create: `XafReportScheduler.Module/DatabaseUpdate/ReportSeeder.cs`
- Modify: `XafReportScheduler.Module/DatabaseUpdate/Updater.cs` (seed data)
- Modify: `XafReportScheduler.Module/BusinessObjects/<DbContext>.cs` (DbSets for Customer, Order; `ReportDataV2` DbSet exists from template — verify)
- Modify: `XafReportScheduler.Module/Module.cs` (`GetModuleUpdaters` returns Updater + ReportSeeder; NO `PredefinedReportsUpdater`)

**Interfaces:**
- Produces: `Customer { Guid ID; string Name }`, `Order { Guid ID; string Number; DateTime OrderDate; decimal Amount; string? Description; Customer? Customer }`, `OrdersReport : XtraReport` (parameterless ctor builds layout over `CollectionDataSource { ObjectTypeName = typeof(Order).FullName }`), `ReportSeeder : ModuleUpdater` with `public const string OrdersReportName = "Orders Report"`.

- [ ] **Step 1: Entities** (invoke `xaf-efcore-entities` skill first)

```csharp
// Customer.cs
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafReportScheduler.Module.BusinessObjects;

[DefaultClassOptions]
public class Customer : BaseObject
{
    public virtual string Name { get; set; } = "";
}
```

```csharp
// Order.cs
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafReportScheduler.Module.BusinessObjects;

[DefaultClassOptions]
public class Order : BaseObject
{
    public virtual string Number { get; set; } = "";
    public virtual DateTime OrderDate { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public virtual decimal Amount { get; set; }
    public virtual string? Description { get; set; }
    public virtual Customer? Customer { get; set; }
}
```

Add `DbSet<Customer> Customers` and `DbSet<Order> Orders` to the DbContext. Confirm `DbSet<ReportDataV2>` is present (template adds it with `-m Reports`; if not, add `public DbSet<ReportDataV2> ReportData { get; set; }` with `using DevExpress.Persistent.BaseImpl.EF;`).

- [ ] **Step 2: Code-built report**

```csharp
// Reports/OrdersReport.cs
using DevExpress.Persistent.Base.ReportsV2;
using DevExpress.XtraReports.UI;
using XafReportScheduler.Module.BusinessObjects;

namespace XafReportScheduler.Module.Reports;

// ponytail: layout built in code so the repo has no .Designer.cs/.repx to maintain;
// after seeding, users edit the persisted copy in the end-user designer.
public class OrdersReport : XtraReport
{
    public OrdersReport()
    {
        DataSource = new CollectionDataSource { ObjectTypeName = typeof(Order).FullName };

        var header = new PageHeaderBand { HeightF = 30 };
        header.Controls.Add(Label("Orders", 0, 300, bold: true));
        Bands.Add(header);

        var detail = new DetailBand { HeightF = 25 };
        detail.Controls.Add(Bound("[Number]", 0, 100));
        detail.Controls.Add(Bound("[OrderDate]", 100, 120, "{0:yyyy-MM-dd}"));
        detail.Controls.Add(Bound("[Customer.Name]", 220, 200));
        detail.Controls.Add(Bound("[Amount]", 420, 100, "{0:N2}"));
        Bands.Add(detail);
    }

    static XRLabel Label(string text, float x, float w, bool bold = false) =>
        new() { Text = text, LocationF = new(x, 0), WidthF = w, HeightF = 25,
                Font = bold ? new("Arial", 12, System.Drawing.FontStyle.Bold) : new("Arial", 10) };

    static XRLabel Bound(string expr, float x, float w, string? format = null)
    {
        var l = new XRLabel { LocationF = new(x, 0), WidthF = w, HeightF = 25 };
        l.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", expr));
        if (format is not null) l.TextFormatString = format;
        return l;
    }
}
```

(`Font` type: in 26.1 XtraReports use `DevExpress.Drawing.DXFont` — check the compiler; if `XRControl.Font` is `DXFont`, write `new DXFont("Arial", 12, DXFontStyle.Bold)`.)

- [ ] **Step 3: Seeder — write the report as a NON-predefined row**

```csharp
// DatabaseUpdate/ReportSeeder.cs
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.BaseImpl.EF;
using XafReportScheduler.Module.Reports;

namespace XafReportScheduler.Module.DatabaseUpdate;

// Seeds code-authored reports as ordinary user reports (IsPredefined == false) so they
// stay editable in the end-user designer. Seed-once: an existing DisplayName is never
// overwritten, so user edits survive restarts and upgrades.
public class ReportSeeder(IObjectSpace objectSpace, Version currentDBVersion, IReportStorage reportStorage)
    : ModuleUpdater(objectSpace, currentDBVersion)
{
    public const string OrdersReportName = "Orders Report";

    public override void UpdateDatabaseAfterUpdateSchema()
    {
        base.UpdateDatabaseAfterUpdateSchema();
        if (ObjectSpace.FirstOrDefault<ReportDataV2>(r => r.DisplayName == OrdersReportName) is not null) return;

        var row = ObjectSpace.CreateObject<ReportDataV2>();
        using var report = new OrdersReport();
        reportStorage.SaveReport((IReportDataV2Writable)row, report);   // sets Content + DataType from the data source
        ((IReportDataV2Writable)row).SetDisplayName(OrdersReportName);
        ObjectSpace.CommitChanges();
    }
}
```

Verify `IReportDataV2Writable` member names (`SetDisplayName`, `SetDataType`, `SetContent`) in `C:\Program Files\DevExpress 26.1\Components\Sources\DevExpress.ExpressApp\DevExpress.Persistent.Base\ReportsV2\IReportDataV2.cs` (or wherever `grep -r "interface IReportDataV2Writable"` finds it). If `SaveReport` does not set the data type, call `SetDataType(typeof(Order))` explicitly.

`Module.cs`:

```csharp
public override IEnumerable<ModuleUpdater> GetModuleUpdaters(IObjectSpace objectSpace, Version versionFromDB)
{
    var storage = ReportDataProvider.GetReportStorage(Application.ServiceProvider);
    return new ModuleUpdater[] {
        new DatabaseUpdate.Updater(objectSpace, versionFromDB),
        new DatabaseUpdate.ReportSeeder(objectSpace, versionFromDB, storage),
    };
}
```

If `Application.ServiceProvider` is null at this point, resolve the storage lazily inside `ReportSeeder.UpdateDatabaseAfterUpdateSchema` by passing `Application` instead of the storage.

- [ ] **Step 4: Seed data in `Updater.UpdateDatabaseAfterUpdateSchema`** (keep the template's Admin/user seeding)

```csharp
if (!ObjectSpace.GetObjects<Customer>().Any())
{
    var acme = ObjectSpace.CreateObject<Customer>();  acme.Name = "Acme Corp";
    var globex = ObjectSpace.CreateObject<Customer>(); globex.Name = "Globex";
    var today = DateTime.Today;
    Seed("ORD-001", today.AddDays(-2),  1250m, acme,   "Recent Acme order — matches last-7-days + Acme*");
    Seed("ORD-002", today.AddDays(-30),  900m, acme,   "Old Acme order — excluded by date");
    Seed("ORD-003", today.AddDays(-1),   300m, globex, "Recent Globex order — excluded by name");
    ObjectSpace.CommitChanges();
}

void Seed(string no, DateTime date, decimal amount, Customer c, string desc)
{
    var o = ObjectSpace.CreateObject<Order>();
    o.Number = no; o.OrderDate = date; o.Amount = amount; o.Customer = c; o.Description = desc;
}
```

- [ ] **Step 5: Build, run, verify non-predefined**

Run: `dotnet build XafReportScheduler.slnx -v q --nologo` → 0 errors.
Start the app (port 5000), wait for HTTP 200, then:

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d XafReportScheduler -Q "SELECT DisplayName, DataTypeName, PredefinedReportTypeName, LEN(Content) AS Bytes FROM ReportDataV2"
```
Expected: one row `Orders Report`, `DataTypeName` = `XafReportScheduler.Module.BusinessObjects.Order`, `PredefinedReportTypeName` NULL, `Bytes` > 0. Log in as `Admin` (empty password), open **Reports** → `Orders Report` → **Show**: three rows visible. Stop the app; free the port.

- [ ] **Step 6: Commit**

```bash
git add XafReportScheduler.Module
git commit -m "feat: sample Customer/Order domain, code-built OrdersReport seeded as editable (non-predefined) ReportDataV2"
```

---

### Task 3: ReportSchedule entity with criteria editor

**Files:**
- Create: `XafReportScheduler.Module/BusinessObjects/ReportSchedule.cs`
- Modify: DbContext (`DbSet<ReportSchedule> ReportSchedules`)
- Modify: `XafReportScheduler.Module/DatabaseUpdate/ReportSeeder.cs` (seed two schedules)

**Interfaces:**
- Produces:

```csharp
public enum ExportFormat { Pdf, Xlsx, Csv }
public class ReportSchedule : BaseObject {
  string Name; ReportDataV2? Report; string? Criteria; string CronExpression; ExportFormat Format;
  string? OutputFolder; bool IsEnabled; DateTime? LastRunUtc; string? LastRunStatus; string? LastRunMessage; string? LastOutputPath;
  Type? ObjectType  // NotMapped, = Report?.DataType — drives [CriteriaOptions]
}
```
- Seeded rows: `"Weekly Acme Orders"` (Pdf, cron `0 7 * * 6`, enabled) and `"E2E Acme Orders (CSV)"` (Csv, cron `0 7 * * 6`, **disabled**) — both `Criteria = "[OrderDate] >= LocalDateTimeLastWeek() And [Customer.Name] Like 'Acme%'"`, `Report` = the seeded Orders Report.

- [ ] **Step 1: Entity** (invoke `xaf-efcore-entities`)

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.Validation;

namespace XafReportScheduler.Module.BusinessObjects;

public enum ExportFormat { Pdf, Xlsx, Csv }

[DefaultClassOptions, NavigationItem("Reports"), ImageName("BO_Scheduler")]
public class ReportSchedule : BaseObject
{
    [RuleRequiredField]
    public virtual string Name { get; set; } = "";

    [RuleRequiredField, ImmediatePostData]
    public virtual ReportDataV2? Report { get; set; }

    // ObjectType feeds the criteria editor; it follows the selected report's data type.
    [NotMapped, Browsable(false)]
    public Type? ObjectType => Report?.DataType;

    [CriteriaOptions(nameof(ObjectType))]
    [FieldSize(FieldSizeAttribute.Unlimited)]
    [EditorAlias(EditorAliases.PopupCriteriaPropertyEditor)]
    public virtual string? Criteria { get; set; }

    [RuleRequiredField, ToolTip("Hangfire cron: minute hour day month weekday. '0 7 * * 6' = Saturdays 07:00 (server local time).")]
    public virtual string CronExpression { get; set; } = "0 7 * * 6";

    public virtual ExportFormat Format { get; set; } = ExportFormat.Pdf;

    [ToolTip("Leave empty to use ReportJobs:OutputFolder from configuration.")]
    public virtual string? OutputFolder { get; set; }

    public virtual bool IsEnabled { get; set; } = true;

    public virtual DateTime? LastRunUtc { get; set; }
    public virtual string? LastRunStatus { get; set; }
    [FieldSize(FieldSizeAttribute.Unlimited)]
    public virtual string? LastRunMessage { get; set; }
    public virtual string? LastOutputPath { get; set; }
}
```

Note `Report.DataType` is a `Type` property on `ReportDataV2` (EF) — verify it exists; if only `DataTypeName` exists, resolve via `XafTypesInfo.Instance.FindTypeInfo(Report.DataTypeName)?.Type`.

- [ ] **Step 2: Seed schedules in `ReportSeeder`** (after the report row exists; guard by Name)

```csharp
void SeedSchedule(string name, ExportFormat fmt, bool enabled)
{
    if (ObjectSpace.FirstOrDefault<ReportSchedule>(s => s.Name == name) is not null) return;
    var s = ObjectSpace.CreateObject<ReportSchedule>();
    s.Name = name;
    s.Report = ObjectSpace.FirstOrDefault<ReportDataV2>(r => r.DisplayName == OrdersReportName);
    s.Criteria = "[OrderDate] >= LocalDateTimeLastWeek() And [Customer.Name] Like 'Acme%'";
    s.CronExpression = "0 7 * * 6";
    s.Format = fmt;
    s.IsEnabled = enabled;
}
// in UpdateDatabaseAfterUpdateSchema, after the report seed:
SeedSchedule("Weekly Acme Orders", ExportFormat.Pdf, enabled: true);
SeedSchedule("E2E Acme Orders (CSV)", ExportFormat.Csv, enabled: false);   // ponytail: E2E fixture; CSV is assertable, PDF is not
ObjectSpace.CommitChanges();
```

- [ ] **Step 3: Build + visual check**

Build → 0 errors. Run the app, log in, open **Report Schedules** → `Weekly Acme Orders`. The Criteria field must show the popup criteria editor bound to `Order` fields (open it, confirm `Order Date`, `Customer.Name` appear). Take one screenshot (Playwright or manual) into `docs/screenshots/schedule-detail.png`. Stop the app.

- [ ] **Step 4: Commit**

```bash
git add XafReportScheduler.Module docs/screenshots
git commit -m "feat: ReportSchedule entity with XAF criteria editor, seeded weekly + E2E schedules"
```

---

### Task 4: ReportJob + Hangfire scheduling + Run Now

**Files:**
- Create: `XafReportScheduler.Module/Services/ReportJob.cs`
- Create: `XafReportScheduler.Blazor.Server/Services/ReportScheduleJobs.cs`
- Create: `XafReportScheduler.Blazor.Server/Services/ReportScheduleSyncService.cs`
- Create: `XafReportScheduler.Blazor.Server/Controllers/ReportScheduleController.cs`
- Modify: `XafReportScheduler.Blazor.Server/Startup.cs`

**Interfaces:**
- Consumes: `ReportSchedule`, `ReportDataV2`, `IReportExportService` (`LoadReport<ReportDataV2>(Func<ReportDataV2,bool>)`, `SetupReport(XtraReport, string criteria, SortProperty[])` — signatures verified in dxdocs topic 113601).
- Produces: `ReportJob.Run(Guid scheduleId)` (public, sync, throws on failure so Hangfire records it); `ReportScheduleJobs.Register(ReportSchedule)`, `ReportScheduleJobs.Remove(Guid)`, `ReportScheduleJobs.RunNow(Guid)`; job id convention `$"report-schedule-{id:N}"`.

- [ ] **Step 1: ReportJob (Module)**

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Core;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Xpo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using XafReportScheduler.Module.BusinessObjects;

namespace XafReportScheduler.Module.Services;

public sealed class ReportJob(
    IReportExportService exportService,
    INonSecuredObjectSpaceFactory nonSecuredFactory,
    ISecurityStrategyBase security,
    IConfiguration config,
    ILogger<ReportJob> log)
{
    public void Run(Guid scheduleId)
    {
        Logon();
        using var os = nonSecuredFactory.CreateNonSecuredObjectSpace<ReportSchedule>();
        var schedule = os.GetObjectByKey<ReportSchedule>(scheduleId)
            ?? throw new InvalidOperationException($"ReportSchedule {scheduleId} not found");
        try
        {
            var path = Export(schedule);
            schedule.LastRunStatus = "Succeeded";
            schedule.LastRunMessage = null;
            schedule.LastOutputPath = path;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Report schedule {Name} failed", schedule.Name);
            schedule.LastRunStatus = "Failed";
            schedule.LastRunMessage = ex.ToString();
            schedule.LastRunUtc = DateTime.UtcNow;
            os.CommitChanges();
            throw;   // Hangfire must see the failure
        }
        schedule.LastRunUtc = DateTime.UtcNow;
        os.CommitChanges();
    }

    string Export(ReportSchedule schedule)
    {
        var reportId = schedule.Report?.ID ?? throw new InvalidOperationException("Schedule has no report");
        using var report = exportService.LoadReport<ReportDataV2>(r => r.ID == reportId);
        // ponytail: criteria string passed straight through; XAF object parameters such as
        // CurrentUserId() would need CriteriaEditorHelper.GetCriteriaOperator first.
        exportService.SetupReport(report, schedule.Criteria ?? string.Empty, Array.Empty<SortProperty>());

        var folder = string.IsNullOrWhiteSpace(schedule.OutputFolder)
            ? config["ReportJobs:OutputFolder"] ?? "output"
            : schedule.OutputFolder;
        Directory.CreateDirectory(folder);
        var safeName = string.Concat(schedule.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(folder, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.{schedule.Format.ToString().ToLowerInvariant()}");
        switch (schedule.Format)
        {
            case ExportFormat.Pdf:  report.ExportToPdf(path);  break;
            case ExportFormat.Xlsx: report.ExportToXlsx(path); break;
            case ExportFormat.Csv:  report.ExportToCsv(path);  break;
        }
        return Path.GetFullPath(path);
    }

    // Hangfire threads have no circuit/user; XAF's secured object spaces need a logged-on user.
    void Logon()
    {
        if (security.IsAuthenticated) return;
        var user = config["ReportJobs:UserName"] ?? "Admin";
        var password = config["ReportJobs:Password"] ?? "";
        if (security is SecurityStrategy concrete)
            concrete.Authentication.SetLogonParameters(new AuthenticationStandardLogonParameters(user, password));
        using var space = nonSecuredFactory.CreateNonSecuredObjectSpace<ApplicationUser>();
        ((SecurityStrategyBase)security).Logon(space);
    }
}
```

`ApplicationUser` is the template's user class — use its actual name. If `IReportExportService` cannot be resolved or throws outside a Blazor circuit, read `DevExpress.ExpressApp.ReportsV2\Services\ReportExportService.cs` in the DX source and replicate its Load/Setup with `ReportDataProvider.GetReportStorage(sp)` + `IReportDataSourceHelper`; report what you found.

- [ ] **Step 2: Hangfire helpers + sync (Blazor.Server)**

```csharp
// Services/ReportScheduleJobs.cs
using Hangfire;
using XafReportScheduler.Module.BusinessObjects;
using XafReportScheduler.Module.Services;

namespace XafReportScheduler.Blazor.Server.Services;

public static class ReportScheduleJobs
{
    public static string JobId(Guid id) => $"report-schedule-{id:N}";

    public static void Register(ReportSchedule s)
    {
        if (s.IsEnabled && !string.IsNullOrWhiteSpace(s.CronExpression))
            RecurringJob.AddOrUpdate<ReportJob>(JobId(s.ID), j => j.Run(s.ID), s.CronExpression,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });
        else
            RecurringJob.RemoveIfExists(JobId(s.ID));
    }

    public static void Remove(Guid id) => RecurringJob.RemoveIfExists(JobId(id));

    public static string RunNow(Guid id) => BackgroundJob.Enqueue<ReportJob>(j => j.Run(id));
}
```

```csharp
// Services/ReportScheduleSyncService.cs
using DevExpress.ExpressApp.Core;
using Microsoft.Extensions.Hosting;
using XafReportScheduler.Module.BusinessObjects;

namespace XafReportScheduler.Blazor.Server.Services;

// ponytail: in-memory Hangfire storage — every startup rebuilds recurring jobs from the
// ReportSchedule table; runs missed while the app was down are not replayed.
public sealed class ReportScheduleSyncService(IServiceProvider sp, ILogger<ReportScheduleSyncService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);   // XAF creates/updates the schema on first request/startup
            try
            {
                using var scope = sp.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
                using var os = factory.CreateNonSecuredObjectSpace<ReportSchedule>();
                var all = os.GetObjects<ReportSchedule>().ToList();
                foreach (var s in all) ReportScheduleJobs.Register(s);
                log.LogInformation("Registered {Count} report schedules", all.Count(s => s.IsEnabled));
                return;
            }
            catch (Exception ex) { log.LogWarning(ex, "Report schedule sync attempt {N} failed", attempt); }
        }
    }
}
```

- [ ] **Step 3: Controller (Blazor.Server)** (invoke `xaf-viewcontroller-patterns`)

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using XafReportScheduler.Blazor.Server.Services;
using XafReportScheduler.Module.BusinessObjects;

namespace XafReportScheduler.Blazor.Server.Controllers;

public class ReportScheduleController : ObjectViewController<DetailView, ReportSchedule>
{
    public ReportScheduleController()
    {
        var run = new SimpleAction(this, "RunReportScheduleNow", PredefinedCategory.Edit)
        { Caption = "Run Now", ImageName = "Action_Debug_Start", ConfirmationMessage = "Run this report now and export it?" };
        run.Execute += (_, _) =>
        {
            var s = ViewCurrentObject;
            if (ObjectSpace.IsModified) ObjectSpace.CommitChanges();
            var jobId = ReportScheduleJobs.RunNow(s.ID);
            Application.ShowViewStrategy.ShowMessage($"Queued (job {jobId}). Refresh to see Last Run fields.", InformationType.Success);
        };
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        ObjectSpace.Committed += ObjectSpace_Committed;
    }
    protected override void OnDeactivated()
    {
        ObjectSpace.Committed -= ObjectSpace_Committed;
        base.OnDeactivated();
    }
    void ObjectSpace_Committed(object? sender, EventArgs e)
    {
        if (ViewCurrentObject is { } s) ReportScheduleJobs.Register(s);   // re-sync cron/enabled on every save
    }
}
```

- [ ] **Step 4: Startup wiring**

In `Startup.ConfigureServices` (after `services.AddXaf(...)`):

```csharp
services.AddHangfire(cfg => cfg.UseInMemoryStorage());
services.AddHangfireServer();
services.AddScoped<XafReportScheduler.Module.Services.ReportJob>();
services.AddHostedService<XafReportScheduler.Blazor.Server.Services.ReportScheduleSyncService>();
```

No dashboard (YAGNI). Confirm `INonSecuredObjectSpaceFactory` and `ISecurityStrategyBase` are resolvable in a plain DI scope (they are in the WLNHeadless pattern this is copied from).

- [ ] **Step 5: Build + live run**

Build → 0 errors. Start the app, log in, open `Report Schedules` → `E2E Acme Orders (CSV)` → **Run Now**. Within ~10 s: `output/E2E Acme Orders (CSV)_<stamp>.csv` exists and contains `ORD-001` and not `ORD-002`/`ORD-003`. Refresh the detail view: `Last Run Status = Succeeded`, `Last Output Path` set. Check the console log for the "Registered 1 report schedules" line. Stop the app.

If the CSV contains all three rows, criteria were not applied — check whether `Like 'Acme%'` needs `StartsWith([Customer.Name], 'Acme')` in the EF Core criteria translator and whether `LocalDateTimeLastWeek()` translated; fix the seeded criteria string accordingly and report which form works.

- [ ] **Step 6: Commit**

```bash
git add XafReportScheduler.Module/Services XafReportScheduler.Blazor.Server
git commit -m "feat: ReportJob export via IReportExportService, Hangfire recurring sync + Run Now"
```

---

### Task 5: E2E phase gate (C# Playwright console)

**Files:**
- Create: `XafReportScheduler.E2ETests/XafReportScheduler.E2ETests.csproj`
- Create: `XafReportScheduler.E2ETests/Program.cs`
- Modify: `XafReportScheduler.slnx` (add project)

**Interfaces:**
- Consumes: app on port 5100 (set `ASPNETCORE_URLS=http://localhost:5100` when starting), LocalDB catalog `XafReportScheduler`, seeded names from Tasks 2–3, output folder `output` under the Blazor.Server content root.

- [ ] **Step 1: Project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Playwright" Version="1.49.0" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.2" />
  </ItemGroup>
</Project>
```

Then `dotnet sln XafReportScheduler.slnx add XafReportScheduler.E2ETests` and, once built, `pwsh XafReportScheduler.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium` (skip if Chromium is already installed for Playwright 1.49 — XafReportParametersObjects used the same version).

- [ ] **Step 2: Script** (`Program.cs`, top-level; model on `C:\Projects\XafReportParametersObjects\XafReportParametersObjects\XafReportParametersObjects.E2ETests\Program.cs` for `StartApp`, `WaitForHttpOk`, `Sql`, `Step`, login helpers — copy those helpers verbatim, adjusting names)

Assertions, in order:

1. Pre-clean via SQL: `DELETE FROM Orders; DELETE FROM Customers;` (so the Updater re-seeds fresh relative dates) and delete `output/*.csv`.
2. Build Blazor.Server; start on 5100; wait for HTTP 200.
3. **Editable seed:** SQL `SELECT PredefinedReportTypeName FROM ReportDataV2 WHERE DisplayName = 'Orders Report'` → must be NULL/empty. Browser: log in (`Admin`, empty password), go to `/ReportDataV2_ListView`, open `Orders Report`, click the design/**Edit** action, assert the report designer surface renders (wait for a `.dxrd-designer` / `dx-report-designer` element — inspect the DOM and use the stable selector found), screenshot `docs/screenshots/e2e-designer.png`.
4. **Criteria + run:** go to `/ReportSchedule_ListView`, open `E2E Acme Orders (CSV)`, click **Run Now**, accept the confirmation, then poll `output/` up to 30 s for a `E2E Acme Orders (CSV)_*.csv`. Read it: must contain `ORD-001`; must NOT contain `ORD-002` or `ORD-003`. Reload the detail view and assert `Succeeded` is visible. Screenshot `docs/screenshots/e2e-schedule-run.png`.
5. **Scheduling registered:** the app's stdout must contain `Registered 1 report schedules` (capture stdout from the started process).
6. Stop the app, free the port, print PASS/FAIL and exit code.

- [ ] **Step 3: Run it**

`dotnet run --project XafReportScheduler.E2ETests`
Expected: all steps PASS. Paste the assertion output into the task report. If a selector is flaky, fix the selector — do not weaken the assertion.

- [ ] **Step 4: Commit**

```bash
git add XafReportScheduler.E2ETests XafReportScheduler.slnx docs/screenshots
git commit -m "test: Playwright E2E gate — editable seed, criteria-filtered CSV via Run Now, schedule sync"
```

---

### Task 6: Docs

**Files:**
- Create: `README.md`, `CLAUDE.md`, `SESSION_HANDOFF.md`

- [ ] **Step 1: README** — sections: What it is (three goals, one paragraph each), Why the seeded report is editable (the `IsPredefined` mechanism, with the dxdocs link), Criteria examples (`LocalDateTimeLastWeek()`, `Like 'Acme%'`/`StartsWith`), Scheduling (cron semantics, local time, in-memory storage caveat), Run (LocalDB, ports, `ReportJobs` config), E2E (how to run), Limitations (folder sink only, no run history, in-memory Hangfire), Licence note (source only; DevExpress packages require your own licence; not a DevExpress product).
- [ ] **Step 2: CLAUDE.md** — project overview, build/run/E2E commands, the four non-negotiables from Global Constraints, and "no TODO.md: this repo is not wired to ContextBoard yet".
- [ ] **Step 3: SESSION_HANDOFF.md** — what was built, what was verified (paste E2E summary), open points: push pending owner go; DevExpress ticket outcome pending (name/EULA); possible next steps (SMTP sink, run history, persistent Hangfire storage, PostgreSQL).
- [ ] **Step 4: Commit**

```bash
git add README.md CLAUDE.md SESSION_HANDOFF.md
git commit -m "docs: README, CLAUDE.md, session handoff"
```

---

## Self-review

- Spec coverage: editable seeded reports → Task 2; criteria parameters → Task 3 (+ applied in Task 4); scheduling → Task 4; "instant" run → Run Now (Task 4); verification → Task 5; docs → Task 6. Private repo, no push → Global Constraints.
- Type consistency: `ReportSeeder.OrdersReportName` used in Tasks 2/3; `ReportJob.Run(Guid)` used by `ReportScheduleJobs` (Task 4) and E2E (Task 5); `ExportFormat` enum values `Pdf/Xlsx/Csv` used in seed + switch; schedule names match between seed and E2E.
- Known risks flagged inline: `IReportDataV2Writable` member names, `XRControl.Font` type in 26.1, `IReportExportService` outside a circuit, EF Core translation of `Like`/`LocalDateTimeLastWeek()`.
