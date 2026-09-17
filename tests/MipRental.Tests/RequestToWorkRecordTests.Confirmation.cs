using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MipRental.Data;
using MipRental.Data.Email;
using MipRental.Data.Interceptors;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Common;
using MipRental.Web.Models.Requests;
using MipRental.Web.Security;

namespace MipRental.Tests;

/// <summary>
/// ADIM 16 BÖLÜM A — TALEP AÇANIN SÜRE TEYİDİ.
///
/// Duruş öncekilerle aynı: "servis hata verdi" bir sonuç değildir, VERİTABANINA
/// bakılır. Kayıt oluştu mu, hangi saatlerle oluştu, talep hangi durumda kaldı,
/// bildirim kime düştü.
/// </summary>
public partial class RequestToWorkRecordTests
{
    private static FakeCurrentUser Requester() =>
        new() { UserId = RequesterId, DepartmentId = DepartmentId, Roles = { RoleNames.Requester } };

    private static FakeCurrentUser OtherRequester() =>
        new() { UserId = 99, DepartmentId = DepartmentId, Roles = { RoleNames.Requester } };

    private static FakeCurrentUser EquipmentManager() =>
        new() { UserId = EquipmentManagerId, Roles = { RoleNames.EquipmentManager } };

    private static IAuthorizationService BuildAuthorizationService() =>
        new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthorizationPolicies.AddAppPolicies)
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();

    /// <summary>Teyit bekleyen iş: operatör bitirdi, talep açan henüz karar vermedi.</summary>
    private static Task<int> SeedAwaitingConfirmationAsync(
        SqliteConnection connection, int variantId = VariantId) =>
        SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 15, 30),
            status: RequestStatus.COMPLETED, variantId: variantId);

    private static Task<int> SeedDisputedAsync(SqliteConnection connection) =>
        SeedCompletedRequestAsync(connection,
            startLocal: Local(2026, 9, 15, 8, 0), endLocal: Local(2026, 9, 15, 15, 30),
            status: RequestStatus.DISPUTED);

    // ---------------------------------------------------------------
    // A7.1 — teyit olmadan türetme yok
    // ---------------------------------------------------------------

    /// <summary>
    /// Adım 12'de COMPLETED yeterliydi; artık değil. Bu test o kapıyı kapatır:
    /// teyit edilmemiş süreden kayıt üretilemez.
    /// </summary>
    [Fact]
    public async Task Derive_FromCompletedButUnconfirmedRequest_IsRejected()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedAwaitingConfirmationAsync(connection);

        var ex = await Assert.ThrowsAsync<Domain.Exceptions.RequestStateTransitionException>(
            () => DeriveAsync(connection, requestId));

        Assert.Contains("Süre Teyit Edildi", ex.Message);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Empty(await db.WorkRecords.AsNoTracking().ToListAsync());
    }

    // ---------------------------------------------------------------
    // A7.2 — talep açan KENDİ talebini teyit eder, başkasınınkini edemez
    // ---------------------------------------------------------------

    [Fact]
    public async Task Confirm_ByRequester_MovesToConfirmed_AndDerivesDraftRecord()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedAwaitingConfirmationAsync(connection);

        await ConfirmAsync(connection, requestId, Requester());

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var request = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(RequestStatus.CONFIRMED, request.Status);
        Assert.NotNull(request.ConfirmationDecisionAt);

        var record = await db.WorkRecords.AsNoTracking().SingleAsync(w => w.RequestId == requestId);
        Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
        Assert.Equal(7.5m, await db.WorkRecordLines.AsNoTracking()
            .Where(l => l.WorkRecordId == record.WorkRecordId).Select(l => l.RawQuantity).SingleAsync());

        // Kaydı "giren" İŞİ BİTİREN OPERATÖRDÜR, teyidi veren MIP personeli değil:
        // yoksa firma kendi kaydının detayında MIP personelinin adını görürdü.
        Assert.Equal(FirmOperatorId, record.EnteredByUserId);

        // Gönderim haberi yine firma yetkilisine düşer (ADR-028).
        var derived = Assert.Single(await db.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.WorkRecordDerived).ToListAsync());
        Assert.Equal(FirmManagerId, derived.UserId);
    }

    /// <summary>
    /// Başkasının talebi bu kullanıcının sorgusundan HİÇ DÖNMEZ (sahiplik sınırı)
    /// ve durum makinesi de aktörün talebi açan kişi olmasını ayrıca arar.
    /// İki katman, ikisi de bağımsız.
    /// </summary>
    [Fact]
    public async Task Confirm_ByAnotherRequester_ChangesNothing()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedAwaitingConfirmationAsync(connection);

        await ConfirmAsync(connection, requestId, OtherRequester());

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.COMPLETED,
            (await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
        Assert.Empty(await db.WorkRecords.AsNoTracking().ToListAsync());
    }

    /// <summary>Durum makinesi tek başına da reddeder — controller es geçilse bile.</summary>
    [Fact]
    public async Task ConfirmDuration_ByAnotherUser_IsRejectedByStateMachine()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedAwaitingConfirmationAsync(connection);

        await using var db = CreateContext(connection, OtherRequester());
        var request = await db.Requests.SingleAsync(r => r.RequestId == requestId);
        var actor = await ApprovalTestFactory.CreateRequestFlowService(db, OtherRequester()).GetActorAsync();
        var period = await ApprovalTestFactory.CreateRequestFlowService(db, OtherRequester())
            .GetPeriodAsync(request.RequestedDate);

        var ex = Assert.Throws<Domain.Exceptions.ApprovalAuthorizationException>(
            () => Domain.Approvals.RequestStateMachine.ConfirmDuration(request, period, actor, DateTime.UtcNow));

        Assert.Contains("talebi açan kişi", ex.Message);
        Assert.Equal(RequestStatus.COMPLETED, request.Status);
    }

    // ---------------------------------------------------------------
    // A7.3 — gerekçesiz itiraz reddedilir
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Dispute_WithoutReason_IsRejected_AndRequestStaysCompleted(string? reason)
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedAwaitingConfirmationAsync(connection);

        var user = Requester();
        string? error;
        await using (var db = CreateContext(connection, user))
        {
            var controller = ApprovalTestFactory.CreateRequestsController(db, user);
            await controller.Dispute(requestId, reason);
            error = controller.TempData[TempDataKeys.ErrorMessage] as string;
        }

        Assert.Contains("İtiraz gerekçesi zorunludur", error);

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var request = await verify.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);
        Assert.Equal(RequestStatus.COMPLETED, request.Status);
        Assert.Null(request.DisputeReason);
    }

    [Fact]
    public async Task Dispute_WithReason_MovesToDisputed_AndNotifiesEquipmentAndFirm()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedAwaitingConfirmationAsync(connection);

        var user = Requester();
        await using (var db = CreateContext(connection, user))
        {
            await ApprovalTestFactory.CreateRequestsController(db, user)
                .Dispute(requestId, "İş 14:00'te bitti, 15:30'da değil");
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var request = await verify.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(RequestStatus.DISPUTED, request.Status);
        Assert.Equal("İş 14:00'te bitti, 15:30'da değil", request.DisputeReason);

        // İtirazdan kayıt TÜREMEZ: süre hâlâ kesin değil.
        Assert.Empty(await verify.WorkRecords.AsNoTracking().ToListAsync());

        var recipients = await verify.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.RequestDisputed)
            .Select(n => n.UserId)
            .ToListAsync();

        Assert.Contains(EquipmentManagerId, recipients);   // hakem
        Assert.Contains(FirmManagerId, recipients);        // süreyi giren taraf
        Assert.All(await verify.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.RequestDisputed).ToListAsync(),
            n => Assert.Contains("İş 14:00'te bitti", n.Body));
    }

    // ---------------------------------------------------------------
    // A7.4 — hakem saati düzeltince türetme DÜZELTİLMİŞ saatle yapılır
    // ---------------------------------------------------------------

    [Fact]
    public async Task ResolveDispute_WithCorrectedHours_DerivesRecordWithCorrectedHours()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedDisputedAsync(connection);

        // 08:00 - 15:30 (7,5 saat) yerine 08:00 - 14:00 (6 saat).
        await ResolveDisputeAsync(connection, requestId,
            correctedStartLocal: new DateTime(2026, 9, 15, 8, 0, 0),
            correctedEndLocal: new DateTime(2026, 9, 15, 14, 0, 0));

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var request = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(RequestStatus.CONFIRMED, request.Status);
        Assert.NotNull(request.DisputeResolvedAt);

        var record = await db.WorkRecords.AsNoTracking().SingleAsync(w => w.RequestId == requestId);
        Assert.Equal(new TimeOnly(8, 0), record.StartTime);
        Assert.Equal(new TimeOnly(14, 0), record.EndTime);

        var line = await db.WorkRecordLines.AsNoTracking().SingleAsync(l => l.WorkRecordId == record.WorkRecordId);
        Assert.Equal(6m, line.RawQuantity);
        Assert.Equal(6m * 1250m, record.TotalAmount);
    }

    /// <summary>Saat düzeltilmezse operatörün girdiği saatler aynen kalır.</summary>
    [Fact]
    public async Task ResolveDispute_WithoutCorrection_KeepsOperatorHours()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedDisputedAsync(connection);

        await ResolveDisputeAsync(connection, requestId, null, null);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var record = await db.WorkRecords.AsNoTracking().SingleAsync(w => w.RequestId == requestId);

        Assert.Equal(new TimeOnly(8, 0), record.StartTime);
        Assert.Equal(new TimeOnly(15, 30), record.EndTime);
    }

    /// <summary>
    /// A5 — saat düzeltmesi DENETİM İZİNE düşer ve KİMİN düzelttiği görünür.
    /// Ayrı bir "düzelten kullanıcı" sütunu açılmadı; alan bazlı denetim izi
    /// eski/yeni değeri kullanıcısıyla birlikte zaten tutuyor.
    /// </summary>
    [Fact]
    public async Task ResolveDispute_Correction_IsRecordedInAuditTrail_WithActor()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedDisputedAsync(connection);

        await ResolveDisputeAsync(connection, requestId,
            correctedStartLocal: new DateTime(2026, 9, 15, 8, 0, 0),
            correctedEndLocal: new DateTime(2026, 9, 15, 14, 0, 0),
            audited: true);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var entry = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TableName == "Requests"
                     && a.RecordId == requestId
                     && a.FieldName == nameof(Request.ActualEndTime))
            .SingleAsync();

        Assert.Equal(EquipmentManagerId, entry.UserId);
        Assert.NotEqual(entry.OldValue, entry.NewValue);
    }

    // ---------------------------------------------------------------
    // A7.5 — "faturalanmayacak" olan talepten kayıt türemez
    // ---------------------------------------------------------------

    [Fact]
    public async Task CancelDisputed_ClosesRequest_AndDerivesNothing()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedDisputedAsync(connection);

        var user = EquipmentManager();
        await using (var db = CreateContext(connection, user))
        {
            await EquipmentController(db, user)
                .CancelDisputed(requestId, "Vinç gelmedi, iş yapılmadı");
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var request = await verify.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(RequestStatus.CANCELLED, request.Status);
        Assert.Equal("Vinç gelmedi, iş yapılmadı", request.CancellationReason);
        Assert.Empty(await verify.WorkRecords.AsNoTracking().ToListAsync());

        var recipients = await verify.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.RequestDisputeCancelled)
            .Select(n => n.UserId).ToListAsync();
        Assert.Contains(RequesterId, recipients);
        Assert.Contains(FirmManagerId, recipients);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task CancelDisputed_WithoutReason_IsRejected(string? reason)
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedDisputedAsync(connection);

        var user = EquipmentManager();
        await using (var db = CreateContext(connection, user))
        {
            await EquipmentController(db, user).CancelDisputed(requestId, reason);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.DISPUTED,
            (await verify.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
    }

    /// <summary>
    /// İtiraz eden kişi kendi itirazını "faturalanmayacak" ilan EDEMEZ; o karar
    /// hakemindir. Genel iptal yolu DISPUTED'ı bilinçli olarak dışarıda bırakır.
    /// </summary>
    [Fact]
    public async Task Cancel_ByRequester_OnDisputedRequest_IsRejected()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedDisputedAsync(connection);

        var user = Requester();
        await using (var db = CreateContext(connection, user))
        {
            await ApprovalTestFactory.CreateRequestsController(db, user)
                .Cancel(requestId, "vazgeçtim");
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.DISPUTED,
            (await verify.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
    }

    // ---------------------------------------------------------------
    // A7.6 — firma ve operatör teyit ekranını GÖREMEZ
    // ---------------------------------------------------------------

    /// <summary>
    /// Teyit ekranı RequestsController'dadır ve sınıf seviyesinde
    /// CanCreateRequest ister; firma rollerinin hiçbiri bu policy'yi geçmez.
    /// Buton gizlemek değil, POLİTİKA değerlendirilerek doğrulanıyor.
    /// </summary>
    [Fact]
    public async Task ConfirmationScreen_IsClosedToFirmRoles()
    {
        var auth = BuildAuthorizationService();

        var firmOperator = Principal(ContractFirmId, RoleNames.FirmOperator);
        var firmManager = Principal(ContractFirmId, RoleNames.FirmManager);
        var requester = Principal(null, RoleNames.Requester);

        Assert.False((await auth.AuthorizeAsync(firmOperator, null, PolicyNames.CanCreateRequest)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(firmManager, null, PolicyNames.CanCreateRequest)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(requester, null, PolicyNames.CanCreateRequest)).Succeeded);

        // Hakemlik ekranı da firma tarafına kapalı.
        Assert.False((await auth.AuthorizeAsync(firmManager, null, PolicyNames.CanViewEquipmentRequests)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(firmOperator, null, PolicyNames.CanDecideEquipmentRequest)).Succeeded);
    }

    /// <summary>
    /// Salt okuyan Ekipman kullanıcısı itiraz listesini GÖRÜR ama karar
    /// action'ları ona kapalıdır (ADR-025'in aynı deseni).
    /// </summary>
    [Theory]
    [InlineData(nameof(Web.Controllers.EquipmentRequestsController.ResolveDispute))]
    [InlineData(nameof(Web.Controllers.EquipmentRequestsController.CancelDisputed))]
    public void DisputeDecisionActions_RequireDecidePolicy(string actionName)
    {
        var action = typeof(Web.Controllers.EquipmentRequestsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == actionName);

        var attribute = Assert.Single(
            action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());

        Assert.Equal(PolicyNames.CanDecideEquipmentRequest, attribute.Policy);
    }

    // ---------------------------------------------------------------
    // A7.7 — teyit ekranında HİÇBİR para alanı yok
    // ---------------------------------------------------------------

    /// <summary>
    /// Gizlenen alan değil, VAR OLMAYAN alan: teyit ve hakemlik modellerinde
    /// decimal bir property ya da para çağrıştıran bir ad bulunmaz.
    /// </summary>
    [Theory]
    [InlineData(typeof(RequestDetailsViewModel))]
    [InlineData(typeof(MyRequestRow))]
    [InlineData(typeof(DisputeResolutionViewModel))]
    [InlineData(typeof(DisputedRequestRow))]
    [InlineData(typeof(DisputedRequestsViewModel))]
    public void ConfirmationModels_CarryNoMoneyField(Type model)
    {
        string[] moneyWords = { "amount", "price", "fee", "total", "currency", "tutar", "fiyat" };

        foreach (var property in model.GetProperties())
        {
            Assert.False(property.PropertyType == typeof(decimal) || property.PropertyType == typeof(decimal?),
                $"{model.Name}.{property.Name} decimal — teyit ekranında para alanı olamaz.");

            Assert.DoesNotContain(moneyWords,
                word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------------------------------------------------------------
    // A6 — hatırlatma ve eskalasyon; OTOMATİK TEYİT ASLA YOK
    // ---------------------------------------------------------------

    [Fact]
    public async Task ConfirmationReminder_IsQueuedOnce_AndNeverAutoConfirms()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedAwaitingConfirmationAsync(connection);

        // İş bitişinden 30 saat sonra: hatırlatma zamanı (varsayılan 24 saat),
        // eskalasyon zamanı değil (72 saat).
        var utcNow = (await LoadRequestAsync(connection, requestId)).ActualEndTime!.Value.AddHours(30);

        await RunSchedulerAsync(connection, utcNow);
        await RunSchedulerAsync(connection, utcNow.AddHours(1));   // ikinci tur

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var reminders = await db.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == ApprovalReminderScheduler.ConfirmationReminderTemplate)
            .ToListAsync();

        var reminder = Assert.Single(reminders);
        Assert.Equal(RequesterId, reminder.UserId);

        // Konu satırında BELGE NUMARASI köşeli parantez içinde (B3).
        var documentNo = (await LoadRequestAsync(connection, requestId)).DocumentNo;
        Assert.StartsWith($"[{documentNo}]", reminder.Subject);

        // Eskalasyon süresi dolmadı.
        Assert.Empty(await db.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == ApprovalReminderScheduler.ConfirmationEscalationTemplate).ToListAsync());

        // CLAUDE.md kural 5: hiçbir talep kendiliğinden teyit edilmez.
        Assert.Equal(RequestStatus.COMPLETED,
            (await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
        Assert.Empty(await db.WorkRecords.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ConfirmationEscalation_GoesToEquipment_AndStillDoesNotConfirm()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedAwaitingConfirmationAsync(connection);

        var utcNow = (await LoadRequestAsync(connection, requestId)).ActualEndTime!.Value.AddHours(100);
        await RunSchedulerAsync(connection, utcNow);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var escalation = Assert.Single(await db.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == ApprovalReminderScheduler.ConfirmationEscalationTemplate).ToListAsync());

        Assert.Equal(EquipmentManagerId, escalation.UserId);

        Assert.Equal(RequestStatus.COMPLETED,
            (await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
    }

    /// <summary>Teyidi verilmiş talep artık hatırlatılmaz.</summary>
    [Fact]
    public async Task ConfirmedRequest_IsNotReminded()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedAwaitingConfirmationAsync(connection);
        await ConfirmAsync(connection, requestId, Requester());

        var utcNow = (await LoadRequestAsync(connection, requestId)).ActualEndTime!.Value.AddHours(100);
        await RunSchedulerAsync(connection, utcNow);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Empty(await db.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == ApprovalReminderScheduler.ConfirmationReminderTemplate
                     || n.TemplateCode == ApprovalReminderScheduler.ConfirmationEscalationTemplate)
            .ToListAsync());
    }

    // ---------------------------------------------------------------
    // Kurulum yardımcıları
    // ---------------------------------------------------------------

    private static async Task ConfirmAsync(SqliteConnection connection, int requestId, FakeCurrentUser user)
    {
        await using var db = CreateContext(connection, user);
        await ApprovalTestFactory.CreateRequestsController(db, user).Confirm(requestId);
    }

    private static async Task ResolveDisputeAsync(
        SqliteConnection connection, int requestId,
        DateTime? correctedStartLocal, DateTime? correctedEndLocal, bool audited = false)
    {
        var user = EquipmentManager();
        await using var db = audited ? CreateAuditedContext(connection, user) : CreateContext(connection, user);
        await EquipmentController(db, user).ResolveDispute(requestId, correctedStartLocal, correctedEndLocal);
    }

    private static Web.Controllers.EquipmentRequestsController EquipmentController(
        AppDbContext db, FakeCurrentUser user) =>
        ApprovalTestFactory.CreateEquipmentRequestsController(
            db, user, BuildAuthorizationService(), Principal(null, RoleNames.EquipmentManager));

    /// <summary>Denetim izi interceptor'ı bağlı bağlam — yalnızca izi sınayan test kullanır.</summary>
    private static AppDbContext CreateAuditedContext(SqliteConnection connection, ICurrentUser user) =>
        new SqliteTestContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(
                    new PeriodGuardInterceptor(),
                    new ImmutabilityGuardInterceptor(),
                    new AuditSaveChangesInterceptor(user, new NoHttpContextAccessor()))
                .Options,
            user);

    private static async Task<Request> LoadRequestAsync(SqliteConnection connection, int requestId)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());
        return await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);
    }

    private static async Task<int> RunSchedulerAsync(SqliteConnection connection, DateTime utcNow)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());
        return await new ApprovalReminderScheduler(db, new NotificationQueue(db), new EmailOptions())
            .RunAsync(utcNow);
    }

    private sealed class NoHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
