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
