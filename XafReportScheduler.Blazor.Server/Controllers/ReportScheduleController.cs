using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
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
        ObjectSpace.Committed += ObjectSpace_Committed;
    }
    protected override void OnDeactivated()
    {
        ObjectSpace.Committed -= ObjectSpace_Committed;
        ObjectSpace.ObjectDeleted -= ObjectSpace_ObjectDeleted;
        base.OnDeactivated();
    }
    void ObjectSpace_ObjectDeleted(object? sender, ObjectsManipulatingEventArgs e)
    {
        foreach (var obj in e.Objects)
            if (obj is ReportSchedule s) deletedIds.Add(s.ID);
    }
    void ObjectSpace_Committed(object? sender, EventArgs e)
    {
        foreach (var id in deletedIds) ReportScheduleJobs.Remove(id);
        deletedIds.Clear();
        if (ViewCurrentObject is { } s && !ObjectSpace.IsDeletedObject(s))
            ReportScheduleJobs.Register(s);   // re-sync cron/enabled on every save
    }
}
