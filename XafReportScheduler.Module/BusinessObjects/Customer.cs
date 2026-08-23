using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafReportScheduler.Module.BusinessObjects;

[DefaultClassOptions]
public class Customer : BaseObject {
    public virtual string Name { get; set; } = "";
}
