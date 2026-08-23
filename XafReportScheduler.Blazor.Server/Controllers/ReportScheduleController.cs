using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.Logging;
using XafReportScheduler.Blazor.Server.Services;
using XafReportScheduler.Module.BusinessObjects;

namespace XafReportScheduler.Blazor.Server.Controllers;

// ObjectView (not DetailView) so this is also active on the ListView -- deleting a schedule
// there must still unregister its Hangfire recurring job.
public class ReportScheduleController : ObjectViewController<ObjectView, ReportSchedule>
{
    readonly HashSet<Guid> deletedIds = new();

    public ReportScheduleController()
    {
        var run = new SimpleAction(this, "RunReportScheduleNow", PredefinedCategory.Edit)
        { Caption = "Run Now", ImageName = "Action_Debug_Start", ConfirmationMessage = "Run this report now and export it?",
          TargetViewType = ViewType.DetailView };
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
        ObjectSpace.ObjectDeleted += ObjectSpace_ObjectDeleted;
        ObjectSpace.Committing += ObjectSpace_Committing;
        ObjectSpace.Committed += ObjectSpace_Committed;
    }
    protected override void OnDeactivated()
    {
        ObjectSpace.Committed -= ObjectSpace_Committed;
        ObjectSpace.Committing -= ObjectSpace_Committing;
        ObjectSpace.ObjectDeleted -= ObjectSpace_ObjectDeleted;
        base.OnDeactivated();
    }
    void ObjectSpace_ObjectDeleted(object? sender, ObjectsManipulatingEventArgs e)
    {
        foreach (var obj in e.Objects)
            if (obj is ReportSchedule s) deletedIds.Add(s.ID);
    }
    // Reject an unparsable cron string at save time rather than letting it silently fail
    // registration later (see ReportScheduleSyncService / ReportScheduleJobs.Register).
    void ObjectSpace_Committing(object? sender, CancelEventArgs e)
    {
        foreach (var s in ObjectSpace.ModifiedObjects.OfType<ReportSchedule>())
        {
            if (string.IsNullOrWhiteSpace(s.CronExpression)) continue;
            try { Cronos.CronExpression.Parse(s.CronExpression); }
            catch (Cronos.CronFormatException ex)
            {
                throw new UserFriendlyException($"Invalid cron expression '{s.CronExpression}': {ex.Message}");
            }
        }
    }
    void ObjectSpace_Committed(object? sender, EventArgs e)
    {
        foreach (var id in deletedIds) ReportScheduleJobs.Remove(id);
        deletedIds.Clear();
        if (ViewCurrentObject is { } s && !ObjectSpace.IsDeletedObject(s))
        {
            try { ReportScheduleJobs.Register(s); }
            catch (Exception ex) { ReportScheduleJobs.Logger?.LogError(ex, "Schedule {Name}: registration failed (bad cron?), skipped", s.Name); }
        }
    }
}
