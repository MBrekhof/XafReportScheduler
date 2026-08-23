using DevExpress.ExpressApp.EFCore.Updating;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.ExpressApp.Design;
using DevExpress.ExpressApp.EFCore.DesignTime;

namespace XafReportScheduler.Module.BusinessObjects;


[TypesInfoInitializer(typeof(DbContextTypesInfoInitializer<XafReportSchedulerEFCoreDbContext>))]
public class XafReportSchedulerEFCoreDbContext : DbContext {
    public XafReportSchedulerEFCoreDbContext(DbContextOptions<XafReportSchedulerEFCoreDbContext> options) : base(options) {
    }
    //public DbSet<ModuleInfo> ModulesInfo { get; set; }
    public DbSet<ModelDifference> ModelDifferences { get; set; }
    public DbSet<ModelDifferenceAspect> ModelDifferenceAspects { get; set; }
    public DbSet<PermissionPolicyRole> Roles { get; set; }
    public DbSet<XafReportScheduler.Module.BusinessObjects.ApplicationUser> Users { get; set; }
    public DbSet<XafReportScheduler.Module.BusinessObjects.ApplicationUserLoginInfo> UserLoginsInfo { get; set; }
    public DbSet<ReportDataV2> ReportDataV2 { get; set; }
    public DbSet<XafReportScheduler.Module.BusinessObjects.Customer> Customers { get; set; }
    public DbSet<XafReportScheduler.Module.BusinessObjects.Order> Orders { get; set; }
    public DbSet<XafReportScheduler.Module.BusinessObjects.ReportSchedule> ReportSchedules { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseDeferredDeletion(this);
        modelBuilder.UseOptimisticLock();
        modelBuilder.SetOneToManyAssociationDeleteBehavior(DeleteBehavior.SetNull, DeleteBehavior.Cascade);
        modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
        modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);
        modelBuilder.Entity<XafReportScheduler.Module.BusinessObjects.ApplicationUserLoginInfo>(b => {
            b.HasIndex(nameof(DevExpress.ExpressApp.Security.ISecurityUserLoginInfo.LoginProviderName), nameof(DevExpress.ExpressApp.Security.ISecurityUserLoginInfo.ProviderUserKey)).IsUnique();
        });
        modelBuilder.Entity<ModelDifference>()
            .HasMany(t => t.Aspects)
            .WithOne(t => t.Owner)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
