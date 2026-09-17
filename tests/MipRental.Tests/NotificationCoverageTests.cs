using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MipRental.Data;
using MipRental.Data.Approvals;
using MipRental.Data.Email;
using MipRental.Data.Interceptors;
using MipRental.Data.Reporting;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Controllers;
using MipRental.Web.Models.Requests;
using MipRental.Web.Security;

namespace MipRental.Tests;

/// <summary>
/// ADIM 16 BÖLÜM B — BİLDİRİM KAPSAMI.
///
/// Envanter tablosunun testi burada: talep açmaktan hakediş onayına kadar TAM
/// DÖNGÜ yürütülür ve her durum geçişinden sonra kuyruğa NE düştüğü, KİME
/// düştüğü tek tek doğrulanır. "Bildirim üretiliyor" demek yetmez; alıcı yanlışsa
/// bildirim yokmuş gibidir — Adım 15'te bulunan fiyat sızıntısının kardeşi budur.
///
/// Fiyat gizliliği burada da sınanır: alt yüklenici ve Ekipman Müdürlüğü'ne giden
/// hiçbir satırda tutar geçmez (ADR-016).
/// </summary>
public class NotificationCoverageTests
{
    private const int FirmId = 1;
    private const int DepartmentId = 1;
    private const int LocationId = 1;
    private const int ServiceId = 1;      // seed: Mobil Vinç, birim HOUR
    private const int VariantId = 1;

    private const int RequesterId = 10;
    private const int EquipmentManagerId = 20;
    private const int BudgetManagerId = 21;
    private const int BudgetId = 22;
    private const int FirmManagerId = 30;
    private const int FirmOperatorId = 31;

    private const decimal UnitPrice = 1250m;

    // İşin gerçekleşen süresi testte SUNUCU saatiyle damgalanır; 4 saatlik bir
    // iş elde etmek için başlangıç geriye çekilir (aşağıda BackdateStartAsync).
    private const decimal ExpectedTotal = 4 * UnitPrice;   // 5.000,00

    // ---------------------------------------------------------------
    // TAM DÖNGÜ — her geçiş, her alıcı
    // ---------------------------------------------------------------

    [Fact]
    public async Task FullCycle_EveryTransition_NotifiesTheRightPeople()
    {
        await using var connection = await SeedAsync();
        var cursor = new NotificationCursor();

        // 1) Talep açıldı ve gönderildi -> Ekipman Müdürlüğü'ne "onayınız bekliyor"
        var requestId = await CreateAndSubmitRequestAsync(connection);
        var submitted = await NewAsync(connection, cursor);
        AssertQueued(submitted, NotificationQueue.Templates.RequestSubmitted, EquipmentManagerId);

        // 2) Ekipman onayladı -> talep açana BİLGİ, firmaya EYLEM ÇAĞRISI
        await ApproveByEquipmentAsync(connection, requestId);
        var equipmentApproved = await NewAsync(connection, cursor);
        AssertQueued(equipmentApproved, NotificationQueue.Templates.RequestEquipmentApproved,
            RequesterId, FirmManagerId, FirmOperatorId);

        // 3) Firma kabul etti -> talep açan + Ekipman + OPERATÖR (sıradaki adım onda)
        await AcceptByFirmAsync(connection, requestId);
        var firmAccepted = await NewAsync(connection, cursor);
        AssertQueued(firmAccepted, NotificationQueue.Templates.RequestFirmAccepted,
            RequesterId, EquipmentManagerId, FirmManagerId, FirmOperatorId);

        // 4) Operatör başladı -> talep açana "işiniz başladı"
        await OperatorAsync(connection, c => c.Start(requestId));
        var started = await NewAsync(connection, cursor);
        AssertQueued(started, NotificationQueue.Templates.RequestStarted, RequesterId);

        // 5) Operatör bitirdi -> talep açana "SÜRE TEYİDİNİZ BEKLENİYOR"
        await OperatorAsync(connection, c => c.Finish(requestId));
        var finished = await NewAsync(connection, cursor);
        AssertQueued(finished, NotificationQueue.Templates.RequestConfirmPending, RequesterId);

        await BackdateStartAsync(connection, requestId, hours: 4);

        // 6) Talep açan teyit etti -> Ekipman'a "teyit edildi", firmaya "gönderim bekliyor"
        await ConfirmAsync(connection, requestId);
        var confirmed = await NewAsync(connection, cursor);
        AssertQueued(confirmed.Where(n => n.TemplateCode == NotificationQueue.Templates.RequestConfirmed),
            NotificationQueue.Templates.RequestConfirmed, EquipmentManagerId);
        AssertQueued(confirmed.Where(n => n.TemplateCode == NotificationQueue.Templates.WorkRecordDerived),
            NotificationQueue.Templates.WorkRecordDerived, FirmManagerId);

        // 7) Firma yetkilisi taslağı tamamlayıp gönderdi -> 1. adımın rolüne
        var recordId = await CompleteAndSubmitRecordAsync(connection, requestId);
        var pendingStep1 = await NewAsync(connection, cursor);
        AssertQueued(pendingStep1, NotificationQueue.Templates.ApprovalPending, EquipmentManagerId);

        // 8) 1. adım onayladı -> 2. adımın rolüne
        await DecideAsync(connection, EquipmentManager(), c => c.Approve(recordId, null));
        var pendingStep2 = await NewAsync(connection, cursor);
        AssertQueued(pendingStep2, NotificationQueue.Templates.ApprovalPending, BudgetManagerId);

        // 9) Son adım onayladı -> ALT YÜKLENİCİYE (firma yetkilisine) sonuç
        await DecideAsync(connection, BudgetManager(), c => c.Approve(recordId, null));
        var approved = await NewAsync(connection, cursor);
        AssertQueued(approved, NotificationQueue.Templates.Approved, FirmManagerId);

        // 10) Bütçe hakedişi kurup yöneticiye gönderdi -> mail onay bağlantısı
        var (paymentId, rawToken) = await CreateAndSendProgressPaymentAsync(connection, recordId);
        var paymentPending = await NewAsync(connection, cursor);
        AssertQueued(paymentPending, NotificationQueue.Templates.ProgressPaymentApproval, BudgetManagerId);

        // 11) Bütçe Yöneticisi mailden onayladı -> hakedişi KURAN Bütçe'ye sonuç
        await DecideProgressPaymentAsync(connection, rawToken, approve: true);
        var paymentApproved = await NewAsync(connection, cursor);
        AssertQueued(paymentApproved, NotificationQueue.Templates.ProgressPaymentApproved, BudgetId);

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.CONFIRMED,
            (await verify.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
        Assert.Equal(WorkRecordStatus.APPROVED,
            (await verify.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == recordId)).Status);
        Assert.Equal(ProgressPaymentStatus.APPROVED,
            (await verify.ProgressPayments.AsNoTracking().SingleAsync(p => p.ProgressPaymentId == paymentId)).Status);
    }

    // ---------------------------------------------------------------
    // İKİNCİ SENARYO — itiraz, saat düzeltme, onay
    // ---------------------------------------------------------------

    [Fact]
    public async Task DisputePath_CorrectedHours_ProduceRecordAndNotifications()
    {
        await using var connection = await SeedAsync();
        var requestId = await CreateAndSubmitRequestAsync(connection);
        await ApproveByEquipmentAsync(connection, requestId);
        await AcceptByFirmAsync(connection, requestId);
        await OperatorAsync(connection, c => c.Start(requestId));
        await OperatorAsync(connection, c => c.Finish(requestId));
        await BackdateStartAsync(connection, requestId, hours: 6);

        var cursor = new NotificationCursor();
        await cursor.SyncAsync(connection);

        // Talep açan itiraz etti -> hakem (Ekipman) + süreyi giren firma
        await using (var db = CreateContext(connection, Requester()))
        {
            await ApprovalTestFactory.CreateRequestsController(db, Requester())
                .Dispute(requestId, "İş 6 saat değil 4 saat sürdü");
        }

        var disputed = await NewAsync(connection, cursor);
        AssertQueued(disputed, NotificationQueue.Templates.RequestDisputed,
            EquipmentManagerId, FirmManagerId, FirmOperatorId);

        // Hakem saati düzeltip onayladı
        var end = (await LoadRequestAsync(connection, requestId)).ActualEndTime!.Value;
        var correctedStartLocal = DateTime.SpecifyKind(end, DateTimeKind.Utc).ToLocalTime().AddHours(-4);

        await using (var db = CreateContext(connection, EquipmentManager()))
        {
            await EquipmentControllerFor(db, EquipmentManager())
                .ResolveDispute(requestId, correctedStartLocal, null);
        }

        var resolved = await NewAsync(connection, cursor);
        AssertQueued(resolved.Where(n => n.TemplateCode == NotificationQueue.Templates.RequestDisputeResolved),
            NotificationQueue.Templates.RequestDisputeResolved,
            RequesterId, FirmManagerId, FirmOperatorId);
        AssertQueued(resolved.Where(n => n.TemplateCode == NotificationQueue.Templates.WorkRecordDerived),
            NotificationQueue.Templates.WorkRecordDerived, FirmManagerId);

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var record = await verify.WorkRecords.AsNoTracking().SingleAsync(w => w.RequestId == requestId);
        var line = await verify.WorkRecordLines.AsNoTracking().SingleAsync(l => l.WorkRecordId == record.WorkRecordId);

        // Kayıt DÜZELTİLMİŞ saatle oluştu: 6 saat değil 4 saat.
        Assert.Equal(4m, line.RawQuantity);
        Assert.Equal(ExpectedTotal, record.TotalAmount);
    }

    // ---------------------------------------------------------------
    // B2 — alt yüklenici ve Ekipman bildirimlerinde TUTAR GEÇMEZ
    // ---------------------------------------------------------------

    /// <summary>
    /// Adım 15'te bulunan sızıntının aynısı tekrarlanmasın: tam döngüde üretilen
    /// bildirimlerin tamamı taranır, fiyat görmeyen rollerin satırlarında tutar
    /// aranır. Tek istisna hakediş onay mailidir; alıcısı Bütçe Yöneticisi'dir.
    /// </summary>
    [Fact]
    public async Task SubcontractorAndEquipmentNotifications_NeverCarryAnAmount()
    {
        await using var connection = await SeedAsync();
        await RunFullCycleAsync(connection);

        int[] priceBlindUsers = { RequesterId, EquipmentManagerId, FirmManagerId, FirmOperatorId };
        string[] amountTraces = { "5000", "5.000", "1250", "1.250", " TL", "TRY" };

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId != null && priceBlindUsers.Contains(n.UserId.Value))
            .ToListAsync();

        Assert.NotEmpty(rows);

        foreach (var row in rows)
        {
            var text = $"{row.Subject} {row.Body}";
            foreach (var trace in amountTraces)
            {
                Assert.DoesNotContain(trace, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Tutar taşıyabilen tek şablon hâlâ hakediş onay mailidir.</summary>
    [Fact]
    public void OnlyProgressPaymentApprovalTemplate_MayContainAmount()
    {
        foreach (var template in AllTemplateCodes())
        {
            var mayContain = EmailTemplates.MayContainAmount(template);
            Assert.Equal(template == NotificationQueue.Templates.ProgressPaymentApproval, mayContain);
        }
    }

    // ---------------------------------------------------------------
    // B3 — konu satırında belge numarası
    // ---------------------------------------------------------------

    [Fact]
    public async Task EverySubject_StartsWithDocumentReferenceInBrackets()
    {
        await using var connection = await SeedAsync();
        await RunFullCycleAsync(connection);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var subjects = await db.Notifications.AsNoTracking().Select(n => n.Subject).ToListAsync();

        Assert.NotEmpty(subjects);
        Assert.All(subjects, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s));
            Assert.StartsWith("[", s);
            Assert.Contains("] ", s);

            // Parantez içi BOŞ olamaz: "[] Talebiniz onaylandı" belge numarası taşımaz.
            var reference = s![1..s.IndexOf(']')];
            Assert.False(string.IsNullOrWhiteSpace(reference));
        });
    }

    // ---------------------------------------------------------------
    // B4 — magic link YAYILMAZ
    // ---------------------------------------------------------------

    /// <summary>
    /// Süre teyidi ve diğer onaylar mail bağlantısıyla verilmez. Oturumsuz karar
    /// yalnızca hakediş onayına açıktır (ADR-030) ve gövdesinde token taşıyan
    /// TEK satır odur.
    /// </summary>
    [Fact]
    public async Task NoNotificationBodyCarriesAnApprovalLink_ExceptProgressPayment()
    {
        await using var connection = await SeedAsync();
        await RunFullCycleAsync(connection);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var rows = await db.Notifications.AsNoTracking().ToListAsync();

        foreach (var row in rows.Where(n => n.TemplateCode != NotificationQueue.Templates.ProgressPaymentApproval))
        {
            Assert.DoesNotContain("/Onay/", row.Body ?? string.Empty, StringComparison.Ordinal);
            Assert.False(EmailTemplates.ContainsSecret(row.TemplateCode));
        }

        var magic = Assert.Single(rows, n => n.TemplateCode == NotificationQueue.Templates.ProgressPaymentApproval);
        Assert.Contains("/Onay/", magic.Body!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gövdeye tek yerden eklenen "uygulamayı aç" bağlantısı NORMAL GİRİŞ ister;
    /// token taşımaz ve magic link mailinde hiç gösterilmez.
    /// </summary>
    [Fact]
    public void AppLinkInTemplate_IsNotAMagicLink()
    {
        var html = EmailTemplates.Render("REQ_CONFIRM_PENDING", "[CPR-1] Süre teyidiniz bekleniyor",
            "Gövde", "https://miprental.mip.com.tr");

        Assert.Contains("https://miprental.mip.com.tr", html);
        Assert.DoesNotContain("/Onay/", html);
        Assert.Contains("giriş yapmanız gerekir", html);

        // Magic link maili ikinci bir bağlantı GÖSTERMEZ.
        var magic = EmailTemplates.Render("PP_APPROVAL_LINK", "[HAK-1] Hakediş",
            "Karar: https://mip.test/Onay/abc", "https://miprental.mip.com.tr");
        Assert.DoesNotContain("Uygulamayı aç", magic);
    }

    // ---------------------------------------------------------------
    // B1 — envanterdeki HER şablonun ekranda/mailde karşılığı var
    // ---------------------------------------------------------------

    /// <summary>
    /// Yeni bir bildirim tipi eklenip başlığı unutulursa mail "MIP Hizmet
    /// Kiralama bildirimi" diye başlıksız gider. Bu test onu yakalar.
    /// </summary>
    [Fact]
    public void EveryTemplate_HasATurkishHeading()
    {
        foreach (var template in AllTemplateCodes())
        {
            Assert.NotEqual("MIP Hizmet Kiralama bildirimi", EmailTemplates.Heading(template));
        }
    }

    private static IEnumerable<string> AllTemplateCodes()
    {
        foreach (var field in typeof(NotificationQueue.Templates).GetFields())
        {
            if (field.IsLiteral && field.FieldType == typeof(string))
            {
                yield return (string)field.GetRawConstantValue()!;
            }
        }

        yield return ApprovalReminderScheduler.ReminderTemplate;
        yield return ApprovalReminderScheduler.EscalationTemplate;
        yield return ApprovalReminderScheduler.ConfirmationReminderTemplate;
        yield return ApprovalReminderScheduler.ConfirmationEscalationTemplate;
    }

    // ---------------------------------------------------------------
    // Akış yardımcıları
    // ---------------------------------------------------------------

    private async Task RunFullCycleAsync(SqliteConnection connection)
    {
        var requestId = await CreateAndSubmitRequestAsync(connection);
        await ApproveByEquipmentAsync(connection, requestId);
        await AcceptByFirmAsync(connection, requestId);
        await OperatorAsync(connection, c => c.Start(requestId));
        await OperatorAsync(connection, c => c.Finish(requestId));
        await BackdateStartAsync(connection, requestId, hours: 4);
        await ConfirmAsync(connection, requestId);

        var recordId = await CompleteAndSubmitRecordAsync(connection, requestId);
        await DecideAsync(connection, EquipmentManager(), c => c.Approve(recordId, null));
        await DecideAsync(connection, BudgetManager(), c => c.Approve(recordId, null));

        var (_, rawToken) = await CreateAndSendProgressPaymentAsync(connection, recordId);
        await DecideProgressPaymentAsync(connection, rawToken, approve: true);
    }

    private static async Task<int> CreateAndSubmitRequestAsync(SqliteConnection connection)
    {
        var user = Requester();
        await using var db = CreateContext(connection, user);
        var controller = ApprovalTestFactory.CreateRequestsController(db, user);

        var result = await controller.Create(new RequestFormViewModel
        {
            RequestedDate = DateOnly.FromDateTime(DateTime.Now),
            RequestedStartTime = new TimeOnly(8, 0),
            EstimatedHours = 4m,
            LocationId = LocationId,
            WorkDescription = "Konteyner taşıma",
            ServiceId = ServiceId,
            VariantId = VariantId
        }, action: "submit");

        return (int)((RedirectToActionResult)result).RouteValues!["id"]!;
    }

    private static async Task ApproveByEquipmentAsync(SqliteConnection connection, int requestId)
    {
        var user = EquipmentManager();
        await using var db = CreateContext(connection, user);
        await EquipmentControllerFor(db, user).Approve(requestId, new EquipmentApprovalModel
        {
            RequestId = requestId,
            RequestedDate = DateOnly.FromDateTime(DateTime.Now),
            RequestedStartTime = new TimeOnly(8, 0),
            VariantId = VariantId,
            FirmId = FirmId
        });
    }

    private static async Task AcceptByFirmAsync(SqliteConnection connection, int requestId)
    {
        var user = FirmManager();
        await using var db = CreateContext(connection, user);
        await ApprovalTestFactory.CreateFirmRequestsController(db, user)
            .Accept(requestId, "Ahmet Yılmaz", "33 ABC 123");
    }

    private static async Task OperatorAsync(
        SqliteConnection connection, Func<FirmOperatorController, Task<IActionResult>> action)
    {
        var user = FirmOperator();
        await using var db = CreateContext(connection, user);
        await action(ApprovalTestFactory.CreateFirmOperatorController(db, user));
    }

    private static async Task ConfirmAsync(SqliteConnection connection, int requestId)
    {
        var user = Requester();
        await using var db = CreateContext(connection, user);
        await ApprovalTestFactory.CreateRequestsController(db, user).Confirm(requestId);
    }

    /// <summary>
    /// "Başladım" ve "Bitirdim" testte milisaniyeler arayla çalıştığı için
    /// gerçekleşen süre sıfıra yakın çıkar. Sunucu saatini geri alamayız;
    /// başlangıç damgasını geriye çekip 4 saatlik bir iş elde ediyoruz.
    /// </summary>
    private static async Task BackdateStartAsync(SqliteConnection connection, int requestId, int hours)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());
        var request = await db.Requests.SingleAsync(r => r.RequestId == requestId);
        request.ActualStartTime = request.ActualEndTime!.Value.AddHours(-hours);
        await db.SaveChangesAsync();
    }

    private static async Task<int> CompleteAndSubmitRecordAsync(SqliteConnection connection, int requestId)
    {
        var user = FirmManager();
        int recordId;

        await using (var read = CreateContext(connection, new FakeCurrentUser()))
        {
            recordId = (await read.WorkRecords.AsNoTracking().SingleAsync(w => w.RequestId == requestId)).WorkRecordId;
        }

        await using (var db = CreateContext(connection, user))
        {
            var controller = ApprovalTestFactory.CreateWorkRecordsController(db, user);
            await controller.CompleteDraft(recordId, 2, "F-1001", DateOnly.FromDateTime(DateTime.Now));
            await controller.Submit(recordId, confirmDuplicate: true);
        }

        return recordId;
    }

    private static async Task DecideAsync(
        SqliteConnection connection, FakeCurrentUser user, Func<ApprovalsController, Task<IActionResult>> action)
    {
        await using var db = CreateContext(connection, user);
        await action(ApprovalTestFactory.CreateApprovalsController(db, user));
    }

    private static async Task<(int PaymentId, string RawToken)> CreateAndSendProgressPaymentAsync(
        SqliteConnection connection, int recordId)
    {
        int periodId;
        await using (var read = CreateContext(connection, new FakeCurrentUser()))
        {
            periodId = (await read.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == recordId)).PeriodId;
        }

        var budget = Budget();
        int paymentId;

        await using (var db = CreateContext(connection, budget))
        {
            var service = CreatePaymentService(db, budget);
            var payment = await service.CreateAsync(periodId, FirmId);
            await service.SendToManagerAsync(payment, "Kontrol edildi.", t => $"https://mip.test/Onay/{t}");
            await db.SaveChangesAsync();
            paymentId = payment.ProgressPaymentId;
        }

        await using var read2 = CreateContext(connection, new FakeCurrentUser());
        var body = await read2.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.ProgressPaymentApproval)
            .OrderBy(n => n.NotificationId)
            .Select(n => n.Body!)
            .FirstAsync();

        const string marker = "https://mip.test/Onay/";
        var start = body.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = body.IndexOfAny(new[] { '\r', '\n', ' ' }, start);

        return (paymentId, end < 0 ? body[start..] : body[start..end]);
    }

    private static async Task DecideProgressPaymentAsync(SqliteConnection connection, string rawToken, bool approve)
    {
        var user = BudgetManager();
        await using var db = CreateContext(connection, user);

        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");

        var controller = new ProgressPaymentApprovalController(
            db, new ApprovalTokenService(db), CreatePaymentService(db, user))
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        await controller.Decide(rawToken, approve ? "approve" : "reject", "Uygundur.", null);
    }

    private static ProgressPaymentService CreatePaymentService(AppDbContext db, ICurrentUser user) =>
        new(db, new MonthlySummaryService(db, user), ApprovalTestFactory.CreateApprovalService(db, user),
            new ApprovalTokenService(db), new NotificationQueue(db));

    private static EquipmentRequestsController EquipmentControllerFor(AppDbContext db, FakeCurrentUser user) =>
        ApprovalTestFactory.CreateEquipmentRequestsController(db, user, BuildAuthorization(),
            new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, RoleNames.EquipmentManager) },
                "Test", System.Security.Claims.ClaimTypes.Name, System.Security.Claims.ClaimTypes.Role)));

    private static Microsoft.AspNetCore.Authorization.IAuthorizationService BuildAuthorization() =>
        new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthorizationPolicies.AddAppPolicies)
            .BuildServiceProvider()
            .GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

    private static async Task<Request> LoadRequestAsync(SqliteConnection connection, int requestId)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());
        return await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);
    }

    // ---------------------------------------------------------------
    // Bildirim okuma
    // ---------------------------------------------------------------

    /// <summary>Son okunan bildirim satırı; her adımda YALNIZCA yeni düşenlere bakılır.</summary>
    private sealed class NotificationCursor
    {
        public long LastId { get; set; }

        public async Task SyncAsync(SqliteConnection connection)
        {
            await using var db = CreateContext(connection, new FakeCurrentUser());
            LastId = await db.Notifications.AsNoTracking().MaxAsync(n => (long?)n.NotificationId) ?? 0;
        }
    }

    private static async Task<List<Notification>> NewAsync(SqliteConnection connection, NotificationCursor cursor)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.NotificationId > cursor.LastId)
            .OrderBy(n => n.NotificationId)
            .ToListAsync();

        if (rows.Count > 0)
        {
            cursor.LastId = rows[^1].NotificationId;
        }

        return rows;
    }

    /// <summary>Beklenen şablon ve ALICILARIN TAM KÜMESİ — fazlası da eksiği de hata.</summary>
    private static void AssertQueued(
        IEnumerable<Notification> rows, string expectedTemplate, params int[] expectedUserIds)
    {
        var list = rows.ToList();

        Assert.NotEmpty(list);
        Assert.All(list, n => Assert.Equal(expectedTemplate, n.TemplateCode));
        Assert.All(list, n => Assert.Equal(NotificationStatus.QUEUED, n.Status));

        Assert.Equal(
            expectedUserIds.OrderBy(id => id).ToArray(),
            list.Select(n => n.UserId!.Value).Distinct().OrderBy(id => id).ToArray());
    }

    // ---------------------------------------------------------------
    // Kurulum
    // ---------------------------------------------------------------

    private static FakeCurrentUser Requester() =>
        new() { UserId = RequesterId, DepartmentId = DepartmentId, Roles = { RoleNames.Requester } };

    private static FakeCurrentUser EquipmentManager() =>
        new() { UserId = EquipmentManagerId, Roles = { RoleNames.EquipmentManager } };

    private static FakeCurrentUser BudgetManager() =>
        new() { UserId = BudgetManagerId, Roles = { RoleNames.BudgetManager } };

    private static FakeCurrentUser Budget() =>
        new() { UserId = BudgetId, Roles = { RoleNames.Budget } };

    private static FakeCurrentUser FirmManager() =>
        new() { UserId = FirmManagerId, FirmId = FirmId, Roles = { RoleNames.FirmManager } };

    private static FakeCurrentUser FirmOperator() =>
        new() { UserId = FirmOperatorId, FirmId = FirmId, Roles = { RoleNames.FirmOperator } };

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser user) =>
        new SqliteTestContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
                .Options,
            user);

    /// <summary>
    /// Tam kadro: talep açan, Ekipman Müdürlüğü, Bütçe, Bütçe Yöneticisi, firma
    /// yetkilisi ve operatör. Onay zinciri model seed'inden gelir (kural 6) —
    /// test kendi zincirini uydurmaz.
    /// </summary>
    private static async Task<SqliteConnection> SeedAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();

        db.Firms.Add(new Firm { FirmId = FirmId, Code = "TESTVINC", Title = "Test Vinç Ltd. Şti.", CreatedAt = DateTime.UtcNow });
        db.Departments.Add(new Department { DepartmentId = DepartmentId, Code = "OPS", Name = "Operasyon" });
        db.Locations.Add(new Location { LocationId = LocationId, Name = "İskele 3", FullPath = "Liman > İskele 3" });

        db.Users.AddRange(
            new User { UserId = RequesterId, UserName = "talep1", FullName = "Talep Eden", Email = "talep1@mip.test", DepartmentId = DepartmentId, CreatedAt = DateTime.UtcNow },
            new User { UserId = EquipmentManagerId, UserName = "ekipman1", FullName = "Ekipman Müdürü", Email = "ekipman1@mip.test", CreatedAt = DateTime.UtcNow },
            new User { UserId = BudgetManagerId, UserName = "butceyon", FullName = "Bütçe Yöneticisi", Email = "butceyon@mip.test", CreatedAt = DateTime.UtcNow },
            new User { UserId = BudgetId, UserName = "butce", FullName = "Bütçe", Email = "butce@mip.test", CreatedAt = DateTime.UtcNow },
            new User { UserId = FirmManagerId, UserName = "yetkili1", FullName = "Firma Yetkilisi", Email = "yetkili1@firma.test", FirmId = FirmId, CreatedAt = DateTime.UtcNow },
            new User { UserId = FirmOperatorId, UserName = "operator1", FullName = "Operatör", Email = "operator1@firma.test", FirmId = FirmId, CreatedAt = DateTime.UtcNow });

        db.UserRoles.AddRange(
            new UserRole { UserId = RequesterId, RoleId = 1 },
            new UserRole { UserId = EquipmentManagerId, RoleId = 2 },
            new UserRole { UserId = BudgetManagerId, RoleId = 3 },
            new UserRole { UserId = BudgetId, RoleId = 4 },
            new UserRole { UserId = FirmManagerId, RoleId = 9 },
            new UserRole { UserId = FirmOperatorId, RoleId = 10 });

        // Sözleşme bugünü kapsar: iş SUNUCU saatiyle damgalanıyor, test hangi
        // tarihte koşarsa koşsun fiyat bulunabilmeli.
        var today = DateOnly.FromDateTime(DateTime.Now);
        db.Contracts.Add(new Contract
        {
            ContractId = 1,
            FirmId = FirmId,
            ContractNo = "SÖZ-2026-001",
            StartDate = today.AddYears(-1),
            EndDate = today.AddYears(1),
            Currency = "TRY",
            Status = ContractStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow
        });
        db.ContractLines.Add(new ContractLine
        {
            ContractLineId = 1,
            ContractId = 1,
            ServiceId = ServiceId,
            VariantId = VariantId,
            UnitPrice = UnitPrice,
            Currency = "TRY",
            ValidFrom = today.AddYears(-1),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        // Dönem satırları 2026 için model seed'inden gelir; test başka bir yılda
        // koşarsa eksik ayları burada açıyoruz (dönem satırının yokluğu türetmeyi
        // durdurur, bkz. RequestToWorkRecordService).
        foreach (var month in new[] { today.AddMonths(-1), today, today.AddMonths(1) })
        {
            if (!await db.Periods.AnyAsync(p => p.Year == month.Year && p.Month == month.Month))
            {
                db.Periods.Add(new Period { Year = month.Year, Month = month.Month, Status = PeriodStatus.OPEN });
            }
        }

        await db.SaveChangesAsync();
        return connection;
    }
}
