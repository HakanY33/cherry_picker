using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Tests;

public class ApprovalIsolationTests
{
    private static AppDbContext CreateContext(string dbName, ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, currentUser);
    }

    private static async Task SeedAsync(string dbName)
    {
        await using var db = CreateContext(dbName, new FakeCurrentUser());

        db.Firms.AddRange(
            new Firm { FirmId = 1, Code = "FIRMA-A", Title = "Firma A", CreatedAt = DateTime.UtcNow },
            new Firm { FirmId = 2, Code = "FIRMA-B", Title = "Firma B", CreatedAt = DateTime.UtcNow });

        db.Users.Add(new User { UserId = 1, UserName = "giren.kullanici", FullName = "Giren Kullanıcı", CreatedAt = DateTime.UtcNow });

        db.Periods.Add(new Period { PeriodId = 1, Year = 2026, Month = 1, Status = PeriodStatus.OPEN });

        db.Contracts.AddRange(
            new Contract { ContractId = 1, FirmId = 1, ContractNo = "C-A", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), CreatedAt = DateTime.UtcNow },
            new Contract { ContractId = 2, FirmId = 2, ContractNo = "C-B", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), CreatedAt = DateTime.UtcNow });

        // WorkRecordId=1 -> Firma A, WorkRecordId=2 -> Firma B
        db.WorkRecords.AddRange(
            new WorkRecord { WorkRecordId = 1, DocumentNo = "WR-1", FirmId = 1, ContractId = 1, PeriodId = 1, WorkDate = new DateOnly(2026, 1, 10), EnteredByUserId = 1, CreatedAt = DateTime.UtcNow },
            new WorkRecord { WorkRecordId = 2, DocumentNo = "WR-2", FirmId = 2, ContractId = 2, PeriodId = 1, WorkDate = new DateOnly(2026, 1, 10), EnteredByUserId = 1, CreatedAt = DateTime.UtcNow });

        // ApprovalId=1 -> Firma A'nın WorkRecord'u, ApprovalId=2 -> Firma B'nin WorkRecord'u
        db.Approvals.AddRange(
            new Approval { ApprovalId = 1, DocumentType = DocumentType.WORK_RECORD, DocumentId = 1, StepNo = 1, AssignedAt = DateTime.UtcNow },
            new Approval { ApprovalId = 2, DocumentType = DocumentType.WORK_RECORD, DocumentId = 2, StepNo = 1, AssignedAt = DateTime.UtcNow });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FirmUser_CannotSeeOtherFirmsApproval()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        // Firma B kullanıcısı, Firma A'nın WorkRecord'una bağlı Approval'ı (ApprovalId=1) sorguluyor.
        await using var db = CreateContext(dbName, new FakeCurrentUser { FirmId = 2 });

        var otherFirmsApproval = await db.Approvals.FirstOrDefaultAsync(a => a.ApprovalId == 1);

        Assert.Null(otherFirmsApproval);
    }

    [Fact]
    public async Task FirmUser_SeesOnlyOwnFirmsApproval()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser { FirmId = 1 });

        var approvals = await db.Approvals.ToListAsync();

        Assert.Single(approvals);
        Assert.Equal(1, approvals[0].ApprovalId);
    }

    [Fact]
    public async Task MipStaff_SeesApprovalsFromBothFirms()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedAsync(dbName);

        await using var db = CreateContext(dbName, new FakeCurrentUser());

        var approvals = await db.Approvals.ToListAsync();

        Assert.Equal(2, approvals.Count);
    }
}
