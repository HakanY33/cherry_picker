using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Entities;

namespace MipRental.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Firm> Firms => Set<Firm>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Location> Locations => Set<Location>();

    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<ServiceVariant> ServiceVariants => Set<ServiceVariant>();
    public DbSet<Equipment> Equipment => Set<Equipment>();

    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractLine> ContractLines => Set<ContractLine>();
    public DbSet<ContractLineSurcharge> ContractLineSurcharges => Set<ContractLineSurcharge>();

    public DbSet<Period> Periods => Set<Period>();
    public DbSet<DocumentSeries> DocumentSeries => Set<DocumentSeries>();

    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RequestLine> RequestLines => Set<RequestLine>();

    public DbSet<WorkRecord> WorkRecords => Set<WorkRecord>();
    public DbSet<WorkRecordLine> WorkRecordLines => Set<WorkRecordLine>();

    public DbSet<ApprovalFlow> ApprovalFlows => Set<ApprovalFlow>();
    public DbSet<ApprovalFlowStep> ApprovalFlowSteps => Set<ApprovalFlowStep>();
    public DbSet<Approval> Approvals => Set<Approval>();

    public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<IntegrationQueue> IntegrationQueue => Set<IntegrationQueue>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // CLAUDE.md: Para decimal(18,4), double/float ASLA kullanma.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("Turkish_100_CI_AS");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyTimestamps()
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Metadata.FindProperty("CreatedAt") is not null)
            {
                entry.Property("CreatedAt").CurrentValue = utcNow;
            }

            if (entry.State == EntityState.Modified && entry.Metadata.FindProperty("UpdatedAt") is not null)
            {
                entry.Property("UpdatedAt").CurrentValue = utcNow;
            }
        }
    }
}
