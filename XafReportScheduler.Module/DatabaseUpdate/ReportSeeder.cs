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
    : ModuleUpdater(objectSpace, currentDBVersion) {
    public const string OrdersReportName = "Orders Report";

    public override void UpdateDatabaseAfterUpdateSchema() {
        base.UpdateDatabaseAfterUpdateSchema();
        if (ObjectSpace.FirstOrDefault<ReportDataV2>(r => r.DisplayName == OrdersReportName) is not null) return;

        var row = ObjectSpace.CreateObject<ReportDataV2>();
        using var report = new OrdersReport();
        reportStorage.SaveReport((IReportDataV2Writable)row, report);   // sets Content + DataType from the data source
        ((IReportDataV2Writable)row).SetDisplayName(OrdersReportName);
        ObjectSpace.CommitChanges();
    }
}
