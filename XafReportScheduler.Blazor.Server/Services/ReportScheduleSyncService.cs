using DevExpress.ExpressApp;
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
