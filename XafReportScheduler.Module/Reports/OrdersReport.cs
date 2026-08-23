using DevExpress.Drawing;
using DevExpress.Persistent.Base.ReportsV2;
using DevExpress.XtraReports.UI;
using XafReportScheduler.Module.BusinessObjects;

namespace XafReportScheduler.Module.Reports;

// ponytail: layout built in code so the repo has no .Designer.cs/.repx to maintain;
// after seeding, users edit the persisted copy in the end-user designer.
public class OrdersReport : XtraReport {
    public OrdersReport() {
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
                Font = bold ? new DXFont("Arial", 12, DXFontStyle.Bold) : new DXFont("Arial", 10) };

    static XRLabel Bound(string expr, float x, float w, string? format = null) {
        var l = new XRLabel { LocationF = new(x, 0), WidthF = w, HeightF = 25 };
        l.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", expr));
        if (format is not null) l.TextFormatString = format;
        return l;
    }
}
