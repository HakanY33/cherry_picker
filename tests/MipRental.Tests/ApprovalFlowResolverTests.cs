using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Approvals;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;

namespace MipRental.Tests;

// CLAUDE.md kural 6: zincir VERİDEN okunur. Bu testler akış seçiminin ve eşik
// süzmesinin tamamen veriye bağlı olduğunu doğrular.
public class ApprovalFlowResolverTests
{
    private const int ServiceId = 1;
    private const int OtherServiceId = 2;

    private static AppDbContext CreateContext(string dbName, ICurrentUser currentUser) =>
        new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options,
            currentUser);

    // InMemory provider HasData seed'ini uygulamaz; akış verisi burada elle kurulur.
    private static async Task<string> SeedAsync(
        bool withDefaultFlow = true,
        bool withServiceFlow = false,
        bool withSecondServiceFlow = false,
        decimal? secondStepThreshold = null,
        bool defaultFlowHasSteps = true)
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateContext(dbName, new FakeCurrentUser());

        db.Roles.AddRange(
            new Role { RoleId = 2, Code = "EQUIPMENT_MANAGER", Name = "Ekipman Müdürlüğü Yöneticisi", Scope = RoleScope.INTERNAL },
            new Role { RoleId = 3, Code = "BUDGET_MANAGER", Name = "Bütçe Yöneticisi", Scope = RoleScope.INTERNAL });

        if (withDefaultFlow)
        {
            db.ApprovalFlows.Add(new ApprovalFlow
            {
                FlowId = 1,
                Code = "WR-DEFAULT",
                Name = "Varsayılan Akış",
                DocumentType = DocumentType.WORK_RECORD,
                ServiceId = null,
                IsActive = true
            });

            if (defaultFlowHasSteps)
            {
                db.ApprovalFlowSteps.AddRange(
                    new ApprovalFlowStep { FlowStepId = 1, FlowId = 1, StepNo = 1, RoleId = 2, Name = "Amir Onayı" },
                    new ApprovalFlowStep
                    {
                        FlowStepId = 2, FlowId = 1, StepNo = 2, RoleId = 3, Name = "Müdür Onayı",
                        AmountThreshold = secondStepThreshold
                    });
            }
        }

        if (withServiceFlow)
        {
            db.ApprovalFlows.Add(new ApprovalFlow
            {
                FlowId = 2,
                Code = "WR-VINC",
                Name = "Vinç Akışı",
                DocumentType = DocumentType.WORK_RECORD,
                ServiceId = ServiceId,
                IsActive = true
            });
            db.ApprovalFlowSteps.Add(new ApprovalFlowStep { FlowStepId = 10, FlowId = 2, StepNo = 1, RoleId = 3, Name = "Vinç Özel Onayı" });
        }

        if (withSecondServiceFlow)
        {
            db.ApprovalFlows.Add(new ApprovalFlow
            {
                FlowId = 3,
                Code = "WR-DIGER",
                Name = "Diğer Hizmet Akışı",
                DocumentType = DocumentType.WORK_RECORD,
                ServiceId = OtherServiceId,
                IsActive = true
            });
            db.ApprovalFlowSteps.Add(new ApprovalFlowStep { FlowStepId = 20, FlowId = 3, StepNo = 1, RoleId = 2, Name = "Diğer Onay" });
        }

        await db.SaveChangesAsync();
        return dbName;
    }

    private static WorkRecord RecordWith(decimal amount, params int[] serviceIds)
    {
        var record = new WorkRecord
        {
            WorkRecordId = 1,
            DocumentNo = "WR-2026-00001",
            FirmId = 1,
            ContractId = 1,
            PeriodId = 1,
            WorkDate = new DateOnly(2026, 3, 10),
            EnteredByUserId = 1,
            TotalAmount = amount
        };

        var lineNo = 1;
        foreach (var serviceId in serviceIds)
        {
            record.WorkRecordLines.Add(new WorkRecordLine { WorkRecordLineId = lineNo, LineNo = lineNo++, ServiceId = serviceId });
        }

        return record;
    }

    [Fact]
    public async Task Resolve_NoServiceSpecificFlow_FallsBackToDefault()
    {
        var dbName = await SeedAsync();
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var chain = await resolver.ResolveForWorkRecordAsync(RecordWith(1000m, ServiceId));

        Assert.Equal("WR-DEFAULT", chain.Flow.Code);
        Assert.Equal(2, chain.Steps.Count);
        Assert.Equal(new[] { 1, 2 }, chain.Steps.Select(s => s.StepNo));
    }

    [Fact]
    public async Task Resolve_ServiceSpecificFlow_WinsOverDefault()
    {
        var dbName = await SeedAsync(withServiceFlow: true);
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var chain = await resolver.ResolveForWorkRecordAsync(RecordWith(1000m, ServiceId));

        Assert.Equal("WR-VINC", chain.Flow.Code);
        Assert.Single(chain.Steps);
    }

    [Fact]
    public async Task Resolve_ServiceWithoutOwnFlow_UsesDefault()
    {
        var dbName = await SeedAsync(withServiceFlow: true);
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        // Kaydın hizmeti (99) hiçbir özel akışa bağlı değil -> varsayılan.
        var chain = await resolver.ResolveForWorkRecordAsync(RecordWith(1000m, 99));

        Assert.Equal("WR-DEFAULT", chain.Flow.Code);
    }

    [Fact]
    public async Task Resolve_StepsAreOrderedByStepNo()
    {
        var dbName = await SeedAsync();
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var chain = await resolver.ResolveForWorkRecordAsync(RecordWith(1000m, ServiceId));

        Assert.Equal(chain.Steps.OrderBy(s => s.StepNo).Select(s => s.StepNo), chain.Steps.Select(s => s.StepNo));
    }

    [Fact]
    public async Task Resolve_ThresholdStep_SkippedBelowThreshold()
    {
        var dbName = await SeedAsync(secondStepThreshold: 5_000m);
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var chain = await resolver.ResolveForWorkRecordAsync(RecordWith(1_000m, ServiceId));

        Assert.Single(chain.Steps);
        Assert.Equal(1, chain.Steps[0].StepNo);
        Assert.Null(chain.StepAfter(1)); // 2. adım hiç yok
    }

    [Fact]
    public async Task Resolve_ThresholdStep_AppliesAboveThreshold()
    {
        var dbName = await SeedAsync(secondStepThreshold: 5_000m);
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var chain = await resolver.ResolveForWorkRecordAsync(RecordWith(5_001m, ServiceId));

        Assert.Equal(2, chain.Steps.Count);
        Assert.Equal(2, chain.StepAfter(1)!.StepNo);
    }

    [Fact]
    public async Task Resolve_ThresholdStep_ExactlyAtThresholdDoesNotApply()
    {
        // "Eşiği AŞIYORSA devreye girer" — eşitlik aşmak değildir.
        var dbName = await SeedAsync(secondStepThreshold: 5_000m);
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var chain = await resolver.ResolveForWorkRecordAsync(RecordWith(5_000m, ServiceId));

        Assert.Single(chain.Steps);
    }

    [Fact]
    public async Task Resolve_NoFlowDefined_ThrowsTurkishError()
    {
        var dbName = await SeedAsync(withDefaultFlow: false);
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var ex = await Assert.ThrowsAsync<ApprovalFlowException>(
            () => resolver.ResolveForWorkRecordAsync(RecordWith(1000m, ServiceId)));

        Assert.Contains("onay akışı yok", ex.Message);
    }

    [Fact]
    public async Task Resolve_FlowWithoutSteps_ThrowsTurkishError()
    {
        var dbName = await SeedAsync(defaultFlowHasSteps: false);
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var ex = await Assert.ThrowsAsync<ApprovalFlowException>(
            () => resolver.ResolveForWorkRecordAsync(RecordWith(1000m, ServiceId)));

        Assert.Contains("hiç adım tanımlı değil", ex.Message);
    }

    [Fact]
    public async Task Resolve_AllStepsFilteredOutByThreshold_ThrowsInsteadOfAutoApproving()
    {
        // CLAUDE.md kural 5: otomatik onay YOKTUR. Hiç adım kalmazsa kayıt
        // sessizce onaylanmaz, hata verilir.
        var dbName = Guid.NewGuid().ToString();
        await using (var seed = CreateContext(dbName, new FakeCurrentUser()))
        {
            seed.Roles.Add(new Role { RoleId = 2, Code = "EQUIPMENT_MANAGER", Name = "Ekipman Müdürlüğü Yöneticisi", Scope = RoleScope.INTERNAL });
            seed.ApprovalFlows.Add(new ApprovalFlow
            {
                FlowId = 1, Code = "WR-DEFAULT", Name = "Varsayılan Akış",
                DocumentType = DocumentType.WORK_RECORD, ServiceId = null, IsActive = true
            });
            seed.ApprovalFlowSteps.Add(new ApprovalFlowStep
            {
                FlowStepId = 1, FlowId = 1, StepNo = 1, RoleId = 2, Name = "Yüksek Tutar Onayı", AmountThreshold = 100_000m
            });
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var ex = await Assert.ThrowsAsync<ApprovalFlowException>(
            () => resolver.ResolveForWorkRecordAsync(RecordWith(400m, ServiceId)));

        Assert.Contains("Eşiksiz en az bir adım", ex.Message);
    }

    [Fact]
    public async Task Resolve_AmbiguousServiceFlows_ThrowsInsteadOfPickingArbitrarily()
    {
        var dbName = await SeedAsync(withServiceFlow: true, withSecondServiceFlow: true);
        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        // Kaydın iki satırı, iki farklı özel akışa işaret ediyor.
        var ex = await Assert.ThrowsAsync<ApprovalFlowException>(
            () => resolver.ResolveForWorkRecordAsync(RecordWith(1000m, ServiceId, OtherServiceId)));

        Assert.Contains("birden fazla onay akışına", ex.Message);
    }

    [Fact]
    public async Task Resolve_InactiveFlowIsIgnored()
    {
        var dbName = await SeedAsync(withServiceFlow: true);
        await using (var update = CreateContext(dbName, new FakeCurrentUser()))
        {
            var serviceFlow = await update.ApprovalFlows.SingleAsync(f => f.Code == "WR-VINC");
            serviceFlow.IsActive = false;
            await update.SaveChangesAsync();
        }

        await using var db = CreateContext(dbName, new FakeCurrentUser());
        var resolver = new ApprovalFlowResolver(db);

        var chain = await resolver.ResolveForWorkRecordAsync(RecordWith(1000m, ServiceId));

        Assert.Equal("WR-DEFAULT", chain.Flow.Code);
    }
}
