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
