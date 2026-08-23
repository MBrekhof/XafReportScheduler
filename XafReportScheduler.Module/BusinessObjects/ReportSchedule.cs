using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.Validation;

namespace XafReportScheduler.Module.BusinessObjects;

public enum ExportFormat { Pdf, Xlsx, Csv }

[DefaultClassOptions, NavigationItem("Reports"), ImageName("BO_Scheduler")]
public class ReportSchedule : BaseObject {
    [RuleRequiredField]
    public virtual string Name { get; set; } = "";

    [RuleRequiredField, ImmediatePostData]
    public virtual ReportDataV2? Report { get; set; }

    // ObjectType feeds the criteria editor; it follows the selected report's data type.
    // ReportDataV2 only exposes DataTypeName publicly (DataType is explicit-interface-only on
    // IReportDataV2) -- resolve the CLR Type via XafTypesInfo, per project convention.
    [NotMapped, Browsable(false)]
    public Type? ObjectType => Report?.DataTypeName is { Length: > 0 } typeName
        ? DevExpress.ExpressApp.XafTypesInfo.Instance.FindTypeInfo(typeName)?.Type
        : null;

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

    [ModelDefault("AllowEdit", "False")]
    [ModelDefault("DisplayFormat", "{0:yyyy-MM-dd HH:mm:ss} UTC")]
    [ModelDefault("EditMask", "yyyy-MM-dd HH:mm:ss")]
    public virtual DateTime? LastRunUtc { get; set; }
    [ModelDefault("AllowEdit", "False")]
    public virtual string? LastRunStatus { get; set; }
    [FieldSize(FieldSizeAttribute.Unlimited), ModelDefault("AllowEdit", "False")]
    public virtual string? LastRunMessage { get; set; }
    [ModelDefault("AllowEdit", "False")]
    public virtual string? LastOutputPath { get; set; }
}
