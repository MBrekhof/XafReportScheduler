using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.BaseImpl.EF;
using XafReportScheduler.Module.BusinessObjects;
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
        // Each seed below guards itself by name, so the report and the two schedules seed
        // independently -- an existing report must not skip schedule seeding (and vice versa).
        if (ObjectSpace.FirstOrDefault<ReportDataV2>(r => r.DisplayName == OrdersReportName) is null) {
            var row = ObjectSpace.CreateObject<ReportDataV2>();
            using var report = new OrdersReport();
            reportStorage.SaveReport((IReportDataV2Writable)row, report);   // sets Content + DataType from the data source
            ((IReportDataV2Writable)row).SetDisplayName(OrdersReportName);
            ObjectSpace.CommitChanges();
        }

        SeedSchedule("Weekly Acme Orders", ExportFormat.Pdf, enabled: true);
        SeedSchedule("E2E Acme Orders (CSV)", ExportFormat.Csv, enabled: false);   // ponytail: E2E fixture; CSV is assertable, PDF is not
        ObjectSpace.CommitChanges();
    }

    void SeedSchedule(string name, ExportFormat fmt, bool enabled) {
        if (ObjectSpace.FirstOrDefault<ReportSchedule>(s => s.Name == name) is not null) return;
        var s = ObjectSpace.CreateObject<ReportSchedule>();
        s.Name = name;
        s.Report = ObjectSpace.FirstOrDefault<ReportDataV2>(r => r.DisplayName == OrdersReportName);
        s.Criteria = "[OrderDate] >= LocalDateTimeLastWeek() And [Customer.Name] Like 'Acme%'";
        s.CronExpression = "0 7 * * 6";
        s.Format = fmt;
        s.IsEnabled = enabled;
    }
}
