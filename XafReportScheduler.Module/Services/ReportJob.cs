using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Core;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Xpo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XafReportScheduler.Module.BusinessObjects;

namespace XafReportScheduler.Module.Services;

public sealed class ReportJob(
    IReportExportService exportService,
    INonSecuredObjectSpaceFactory nonSecuredFactory,
    ISecurityStrategyBase security,
    IConfiguration config,
    IHostEnvironment env,
    ILogger<ReportJob> log)
{
    public void Run(Guid scheduleId)
    {
        using var os = nonSecuredFactory.CreateNonSecuredObjectSpace<ReportSchedule>();
        var schedule = os.GetObjectByKey<ReportSchedule>(scheduleId)
            ?? throw new InvalidOperationException($"ReportSchedule {scheduleId} not found");
        try
        {
            Logon();
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
            try { os.CommitChanges(); }
            catch (Exception commitEx) { log.LogError(commitEx, "Report schedule {Name}: failed to persist failure status", schedule.Name); }
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
        folder = Path.IsPathRooted(folder) ? folder : Path.Combine(env.ContentRootPath, folder);
        Directory.CreateDirectory(folder);
        var safeName = string.Concat(schedule.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(folder, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.{schedule.Format.ToString().ToLowerInvariant()}");
        switch (schedule.Format)
        {
            case ExportFormat.Pdf:  report.ExportToPdf(path);  break;
            case ExportFormat.Xlsx: report.ExportToXlsx(path); break;
            case ExportFormat.Csv:  report.ExportToCsv(path);  break;
            default: throw new NotSupportedException($"Unsupported export format {schedule.Format}");
        }
        return Path.GetFullPath(path);
    }

    // Hangfire threads have no circuit/user; XAF's secured object spaces need a logged-on user.
    void Logon()
    {
        if (security.IsAuthenticated) return;
        if (security is not SecurityStrategy concrete)
            throw new InvalidOperationException($"Unexpected security strategy {security.GetType().Name}");
        var user = config["ReportJobs:UserName"] ?? "Admin";
        var password = config["ReportJobs:Password"] ?? "";
        concrete.Authentication.SetLogonParameters(new AuthenticationStandardLogonParameters(user, password));
        using var space = nonSecuredFactory.CreateNonSecuredObjectSpace<ApplicationUser>();
        concrete.Logon(space);
    }
}
