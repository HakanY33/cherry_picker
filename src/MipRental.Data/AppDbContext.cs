using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data;

public class AppDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser) : base(options)
    {
        _currentUser = currentUser;
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

    public DbSet<ProgressPayment> ProgressPayments => Set<ProgressPayment>();
    public DbSet<ProgressPaymentRecord> ProgressPaymentRecords => Set<ProgressPaymentRecord>();
    public DbSet<ApprovalToken> ApprovalTokens => Set<ApprovalToken>();

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

        ApplyFirmIsolationFilters(modelBuilder);
    }

    // CLAUDE.md kural 7: alt yüklenici sadece kendi firmasının verisini görür.
    // Filtre DbContext seviyesinde uygulanır; controller/service katmanında
    // manuel "if (FirmId != null)" kontrolü YAZILMAZ.
    private void ApplyFirmIsolationFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkRecord>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.FirmId == _currentUser.FirmId);
        modelBuilder.Entity<Request>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.FirmId == _currentUser.FirmId);
        modelBuilder.Entity<Contract>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.FirmId == _currentUser.FirmId);
        modelBuilder.Entity<Equipment>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.FirmId == _currentUser.FirmId);
        modelBuilder.Entity<User>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.FirmId == _currentUser.FirmId);
        // Hakediş ekranı firma kullanıcısına policy ile zaten kapalı; filtre yine de
        // kurulur — kural 7 "her sorguda", "ekran kapalı olduğu için gerek yok" değil.
        modelBuilder.Entity<ProgressPayment>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.FirmId == _currentUser.FirmId);

        // Child entity'ler parent üzerinden filtrelenir (navigation join).
        modelBuilder.Entity<WorkRecordLine>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.WorkRecord.FirmId == _currentUser.FirmId);
        modelBuilder.Entity<RequestLine>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.Request.FirmId == _currentUser.FirmId);
        modelBuilder.Entity<ContractLine>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.Contract.FirmId == _currentUser.FirmId);
        modelBuilder.Entity<ProgressPaymentRecord>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.ProgressPayment.FirmId == _currentUser.FirmId);
        modelBuilder.Entity<ContractLineSurcharge>()
            .HasQueryFilter(x => _currentUser.FirmId == null || x.ContractLine.Contract.FirmId == _currentUser.FirmId);

        // Approval, WorkRecord/Request'e gerçek bir FK/navigation ile değil,
        // DocumentType + DocumentId ile polimorfik bağlanır. Set<T>() üzerinden
        // yapılan varlık kontrolü, ilgili tablonun kendi FirmId filtresinden de
        // geçer; bu yüzden FirmId koşulu burada ayrıca tekrarlanmaz.
        modelBuilder.Entity<Approval>().HasQueryFilter(x =>
            (x.DocumentType == DocumentType.WORK_RECORD && Set<WorkRecord>().Any(w => w.WorkRecordId == x.DocumentId)) ||
            (x.DocumentType == DocumentType.REQUEST && Set<Request>().Any(r => r.RequestId == x.DocumentId)));
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
