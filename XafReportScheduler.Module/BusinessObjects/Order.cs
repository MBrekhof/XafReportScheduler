using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafReportScheduler.Module.BusinessObjects;

[DefaultClassOptions]
public class Order : BaseObject {
    public virtual string Number { get; set; } = "";
    public virtual DateTime OrderDate { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public virtual decimal Amount { get; set; }
    public virtual string? Description { get; set; }
    public virtual Customer? Customer { get; set; }
}
