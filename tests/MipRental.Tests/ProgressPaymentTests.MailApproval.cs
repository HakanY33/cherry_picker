using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Exceptions;
using MipRental.Web.Controllers;
using MipRental.Web.Models.ProgressPayments;

namespace MipRental.Tests;

/// <summary>
/// ADIM 14 BÖLÜM B — MAİL ONAYI (ADR-015).
///
/// Buradaki en kritik test <see cref="Link_Get_ChangesNothing"/>: kurumsal mail
/// tarayıcıları bağlantıları önceden açar, GET onaylasaydı belgeyi kimse
/// görmeden mail sunucusu onaylardı. Test bunu "sayfa açıldı" diye değil,
/// VERİTABANINDA hiçbir şeyin değişmediğini doğrulayarak sabitler.
/// </summary>
public partial class ProgressPaymentTests
{
    private const string TestIp = "203.0.113.9";
    private const string TestUserAgent = "Mozilla/5.0 (Test)";

    private static ProgressPaymentApprovalController ApprovalController(
        AppDbContext db, ICurrentUser user, string ip = TestIp, string userAgent = TestUserAgent)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        http.Request.Headers.UserAgent = userAgent;

        return new ProgressPaymentApprovalController(db, new ApprovalTokenService(db), CreateService(db, user))
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    /// <summary>
    /// Kuyruğa yazılan MAİL GÖVDESİNDEN ham token'ı çıkarır — gerçek akışın
    /// aynısı: "Notifications'taki bağlantıyı kopyala, tarayıcıda aç".
    /// </summary>
    private static async Task<string> MailedTokenAsync(SqliteConnection connection, int paymentId, int index = 0)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());
        var bodies = await db.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.ProgressPaymentApproval
                     && n.DocumentId == paymentId)
            .OrderBy(n => n.NotificationId)
            .Select(n => n.Body!)
            .ToListAsync();

        const string marker = "https://mip.test/Onay/";
        var body = bodies[index];
        var start = body.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = body.IndexOfAny(new[] { '\r', '\n', ' ' }, start);
        return end < 0 ? body[start..] : body[start..end];
    }

    private static async Task<(int PaymentId, string Token)> CreatePendingPaymentWithTokenAsync(SqliteConnection connection)
    {
        await AddRecordAsync(connection, 1, WorkRecordStatus.APPROVED, 1000m);
        var payment = await CreatePaymentAsync(connection);
        await SendToManagerAsync(connection, payment.ProgressPaymentId, "Ağustos icmali kontrol edildi.");

        return (payment.ProgressPaymentId, await MailedTokenAsync(connection, payment.ProgressPaymentId));
    }

    // ---------------------------------------------------------------
    // B2 — EN KRİTİK: GET ONAYLAMAZ
    // ---------------------------------------------------------------

    /// <summary>
    /// Bağlantıya GİRMEK hiçbir şeyi değiştirmez: hakediş hâlâ onay bekler,
    /// token tüketilmemiştir, IP/zaman damgası yazılmamıştır. Outlook ya da
    /// Defender bu adresi taradığında olan tam olarak budur.
    /// </summary>
    [Fact]
    public async Task Link_Get_ChangesNothing()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, token) = await CreatePendingPaymentWithTokenAsync(connection);

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var result = Assert.IsType<ViewResult>(await ApprovalController(db, new FakeCurrentUser()).Index(token));
            var model = Assert.IsType<ApprovalLinkViewModel>(result.Model);

            // Özet gerçekten gösteriliyor (sayfa boş dönmüyor).
            Assert.Equal(1000m, model.TotalAmount);
            Assert.Equal(1, model.RecordCount);
            Assert.Equal("Ağustos icmali kontrol edildi.", model.BudgetNote);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var payment = await verify.ProgressPayments.AsNoTracking().SingleAsync(p => p.ProgressPaymentId == paymentId);
        var stored = await verify.ApprovalTokens.AsNoTracking().SingleAsync(t => t.ProgressPaymentId == paymentId);

        Assert.Equal(ProgressPaymentStatus.PENDING_BUDGET_MANAGER, payment.Status);
        Assert.Null(payment.ManagerApprovedAt);
        Assert.Null(payment.ManagerApprovedByUserId);
        Assert.Null(stored.UsedAt);
        Assert.Null(stored.UsedFromIp);
        Assert.Null(stored.RevokedAt);
    }

    // ---------------------------------------------------------------
    // B1 — token veritabanında DÜZ METİN durmaz
    // ---------------------------------------------------------------

    [Fact]
    public async Task Token_IsStoredAsHash_NeverInPlainText()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, token) = await CreatePendingPaymentWithTokenAsync(connection);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var stored = await db.ApprovalTokens.AsNoTracking().SingleAsync(t => t.ProgressPaymentId == paymentId);

        Assert.Equal(32, stored.TokenHash.Length);
        Assert.Equal(ApprovalTokenService.Hash(token), stored.TokenHash);

        // Ham token tablonun HİÇBİR metin kolonunda geçmiyor.
        var rawRow = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM ApprovalTokens WHERE CAST(TokenHash AS TEXT) LIKE '%' || {token} || '%'")
            .SingleAsync();
        Assert.Equal(0, rawRow);

        // Ham token YALNIZCA mail gövdesinde görünür (bağlantının içinde).
        var body = await db.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.ProgressPaymentApproval)
            .Select(n => n.Body!)
            .SingleAsync();
        Assert.Contains(token, body);
        Assert.Contains("7 gün geçerlidir", body);
    }

    // ---------------------------------------------------------------
    // B3 — hata durumları
    // ---------------------------------------------------------------

    [Fact]
    public async Task UsedToken_DoesNotWorkTwice()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, token) = await CreatePendingPaymentWithTokenAsync(connection);

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            await ApprovalController(db, new FakeCurrentUser()).Decide(token, "approve", note: null, reason: null);
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var result = Assert.IsType<ViewResult>(await ApprovalController(db, new FakeCurrentUser()).Index(token));
            Assert.Equal("Used", result.ViewName);

            // Kararın NE olduğu ve NE ZAMAN verildiği gösterilir.
            var model = Assert.IsType<ApprovalLinkUsedViewModel>(result.Model);
            Assert.Equal(ProgressPaymentStatus.APPROVED, model.Status);
            Assert.NotNull(model.DecidedAt);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(1, await verify.ProgressPayments.CountAsync(p => p.ProgressPaymentId == paymentId
            && p.Status == ProgressPaymentStatus.APPROVED));
    }

    [Fact]
    public async Task ExpiredToken_DoesNotWork()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, token) = await CreatePendingPaymentWithTokenAsync(connection);

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var stored = await db.ApprovalTokens.SingleAsync(t => t.ProgressPaymentId == paymentId);
            stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var controller = ApprovalController(db, new FakeCurrentUser());
            Assert.Equal("Expired", Assert.IsType<ViewResult>(await controller.Index(token)).ViewName);

            // POST da geçmez: süre kontrolü tek yerde, iki yol da oradan geçer.
            Assert.Equal("Expired",
                Assert.IsType<ViewResult>(await controller.Decide(token, "approve", null, null)).ViewName);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(ProgressPaymentStatus.PENDING_BUDGET_MANAGER,
            (await verify.ProgressPayments.AsNoTracking().SingleAsync()).Status);
    }

    /// <summary>Yanlış token bilgi sızdırmaz: 404 + hangi hakediş olduğuna dair tek kelime yok.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("bilinmeyen-token")]
    [InlineData("../../etc/passwd")]
    public async Task UnknownToken_ReturnsNotFound_AndLeaksNothing(string token)
    {
        await using var connection = await CreateSeededConnectionAsync();
        await CreatePendingPaymentWithTokenAsync(connection);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var controller = ApprovalController(db, new FakeCurrentUser());
        var result = Assert.IsType<ViewResult>(await controller.Index(token));

        Assert.Equal("Invalid", result.ViewName);
        Assert.Null(result.Model);                                        // model YOK: sızacak alan da yok
        Assert.Equal(StatusCodes.Status404NotFound, controller.Response.StatusCode);
    }

    /// <summary>
    /// B9 — token TEK bir hakedişe bağlıdır (polimorfik çift değil, FK). Bir
    /// hakedişin bağlantısı diğerine karar veremez.
    /// </summary>
    [Fact]
    public async Task TokenOfOnePayment_CannotDecideAnother()
    {
        await using var connection = await CreateSeededConnectionAsync();

        // Firma 1 hakedişi (token buradan çıkacak).
        var (firstId, token) = await CreatePendingPaymentWithTokenAsync(connection);

        // Firma 2 için ayrı bir hakediş, o da onay bekliyor.
        await AddRecordAsync(connection, 2, WorkRecordStatus.APPROVED, 4000m, OtherFirmId, contractId: 2);
        var second = await CreatePaymentAsync(connection, OtherFirmId);
        await SendToManagerAsync(connection, second.ProgressPaymentId);

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            await ApprovalController(db, new FakeCurrentUser()).Decide(token, "approve", null, null);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(ProgressPaymentStatus.APPROVED,
            (await verify.ProgressPayments.AsNoTracking().SingleAsync(p => p.ProgressPaymentId == firstId)).Status);
        Assert.Equal(ProgressPaymentStatus.PENDING_BUDGET_MANAGER,
            (await verify.ProgressPayments.AsNoTracking().SingleAsync(p => p.ProgressPaymentId == second.ProgressPaymentId)).Status);

        // İkinci hakedişin token'ı da hâlâ el değmemiş.
        Assert.Null((await verify.ApprovalTokens.AsNoTracking()
            .SingleAsync(t => t.ProgressPaymentId == second.ProgressPaymentId)).UsedAt);
    }

    // ---------------------------------------------------------------
    // Karar: onay, red, denetim izi
    // ---------------------------------------------------------------

    [Fact]
    public async Task Approve_ViaLink_SetsApproved_AndLogsIpAndTime()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, token) = await CreatePendingPaymentWithTokenAsync(connection);

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var result = Assert.IsType<ViewResult>(
                await ApprovalController(db, new FakeCurrentUser()).Decide(token, "approve", "Uygundur.", null));
            Assert.Equal("Done", result.ViewName);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var payment = await verify.ProgressPayments.AsNoTracking().SingleAsync(p => p.ProgressPaymentId == paymentId);
        var stored = await verify.ApprovalTokens.AsNoTracking().SingleAsync(t => t.ProgressPaymentId == paymentId);

        Assert.Equal(ProgressPaymentStatus.APPROVED, payment.Status);
        Assert.Equal("Uygundur.", payment.ManagerNote);

        // Kararı VEREN, token'ın gönderildiği yöneticidir — oturum yok.
        Assert.Equal(BudgetManagerUserId, payment.ManagerApprovedByUserId);
        Assert.NotNull(payment.ManagerApprovedAt);

        Assert.NotNull(stored.UsedAt);
        Assert.Equal(TestIp, stored.UsedFromIp);
        Assert.Equal(TestUserAgent, stored.UsedUserAgent);
    }

    /// <summary>Gerekçesiz red geçmez ve TOKEN TÜKENMEZ: kullanıcı düzeltip tekrar dener.</summary>
    [Fact]
    public async Task Reject_ViaLink_WithoutReason_IsRejected_AndTokenSurvives()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, token) = await CreatePendingPaymentWithTokenAsync(connection);

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var result = Assert.IsType<ViewResult>(
                await ApprovalController(db, new FakeCurrentUser()).Decide(token, "reject", null, "   "));

            var model = Assert.IsType<ApprovalLinkViewModel>(result.Model);
            Assert.Contains("gerekçesi zorunludur", model.ErrorMessage);
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var verifyToken = await db.ApprovalTokens.AsNoTracking().SingleAsync(t => t.ProgressPaymentId == paymentId);
            Assert.Null(verifyToken.UsedAt);
            Assert.Equal(ProgressPaymentStatus.PENDING_BUDGET_MANAGER,
                (await db.ProgressPayments.AsNoTracking().SingleAsync()).Status);
        }

        // Gerekçe verilince aynı bağlantı çalışır.
        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            await ApprovalController(db, new FakeCurrentUser()).Decide(token, "reject", null, "Tutar hatalı.");
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var payment = await verify.ProgressPayments.AsNoTracking().SingleAsync();
        Assert.Equal(ProgressPaymentStatus.REJECTED, payment.Status);
        Assert.Equal("Tutar hatalı.", payment.RejectionReason);
    }

    // ---------------------------------------------------------------
    // B8 — geri çekme token'ı İPTAL eder
    // ---------------------------------------------------------------

    /// <summary>
    /// Bütçe hakedişi geri çekerse mail kutusundaki bağlantı DERHAL ölür.
    /// Aksi halde geri çekilmiş bir hakediş eski bağlantıdan onaylanabilirdi.
    /// </summary>
    [Fact]
    public async Task WithdrawnPayment_RevokesToken_AndShowsNotPending()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, token) = await CreatePendingPaymentWithTokenAsync(connection);

        var budget = Budget();
        await using (var db = CreateContext(connection, budget))
        {
            var payment = await db.ProgressPayments.SingleAsync(p => p.ProgressPaymentId == paymentId);
            var revoked = await CreateService(db, budget).WithdrawAsync(payment);
            await db.SaveChangesAsync();

            Assert.Equal(1, revoked);
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var controller = ApprovalController(db, new FakeCurrentUser());

            var get = Assert.IsType<ViewResult>(await controller.Index(token));
            Assert.Equal("NotPending", get.ViewName);
            Assert.Equal(ProgressPaymentStatus.DRAFT,
                Assert.IsType<ApprovalLinkNotPendingViewModel>(get.Model).Status);

            // POST da aynı duvara çarpar.
            Assert.Equal("NotPending",
                Assert.IsType<ViewResult>(await controller.Decide(token, "approve", null, null)).ViewName);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var stored = await verify.ApprovalTokens.AsNoTracking().SingleAsync(t => t.ProgressPaymentId == paymentId);
        Assert.NotNull(stored.RevokedAt);
        Assert.Null(stored.UsedAt);

        // Hakediş taslağa döndü ve Bütçe'nin imzası silindi: yeniden gönderilirken
        // yeniden imzalanacak.
        var withdrawn = await verify.ProgressPayments.AsNoTracking().SingleAsync(p => p.ProgressPaymentId == paymentId);
        Assert.Equal(ProgressPaymentStatus.DRAFT, withdrawn.Status);
        Assert.Null(withdrawn.BudgetApprovedByUserId);
        Assert.Null(withdrawn.BudgetApprovedAt);
    }

    /// <summary>Geri çekilen hakediş yeniden gönderilebilir; YENİ token üretilir.</summary>
    [Fact]
    public async Task WithdrawnPayment_CanBeSentAgain_WithFreshToken()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, firstToken) = await CreatePendingPaymentWithTokenAsync(connection);

        var budget = Budget();
        await using (var db = CreateContext(connection, budget))
        {
            var payment = await db.ProgressPayments.SingleAsync(p => p.ProgressPaymentId == paymentId);
            await CreateService(db, budget).WithdrawAsync(payment);
            await db.SaveChangesAsync();
        }

        await SendToManagerAsync(connection, paymentId, "Düzeltildi.");
        var secondToken = await MailedTokenAsync(connection, paymentId, index: 1);

        Assert.NotEqual(firstToken, secondToken);

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var controller = ApprovalController(db, new FakeCurrentUser());
            Assert.Equal("NotPending", Assert.IsType<ViewResult>(await controller.Index(firstToken)).ViewName);
            Assert.IsType<ApprovalLinkViewModel>(Assert.IsType<ViewResult>(await controller.Index(secondToken)).Model);
        }
    }

    // ---------------------------------------------------------------
    // B5 — yedek yol: arayüzden onay
    // ---------------------------------------------------------------

    /// <summary>Mail kaybolursa karar arayüzden verilir; aynı durum makinesi, aynı sonuç.</summary>
    [Fact]
    public async Task Approve_ViaUi_BackupPath_Works()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, _) = await CreatePendingPaymentWithTokenAsync(connection);

        var manager = BudgetManager();
        await using (var db = CreateContext(connection, manager))
        {
            var payment = await db.ProgressPayments.SingleAsync(p => p.ProgressPaymentId == paymentId);
            await CreateService(db, manager).ApproveAsync(payment, "Ekrandan onaylandı.");
            await db.SaveChangesAsync();
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var stored = await verify.ProgressPayments.AsNoTracking().SingleAsync(p => p.ProgressPaymentId == paymentId);

        Assert.Equal(ProgressPaymentStatus.APPROVED, stored.Status);
        Assert.Equal(BudgetManagerUserId, stored.ManagerApprovedByUserId);
        Assert.Equal("Ekrandan onaylandı.", stored.ManagerNote);
    }

    /// <summary>
    /// Ekrandan karar verildikten sonra maildeki bağlantı artık karar veremez:
    /// token iptal edilmese de hakediş PENDING değil (durum kapısı tek yerde).
    /// </summary>
    [Fact]
    public async Task AfterUiDecision_MailLinkCannotDecideAgain()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var (paymentId, token) = await CreatePendingPaymentWithTokenAsync(connection);

        var manager = BudgetManager();
        await using (var db = CreateContext(connection, manager))
        {
            var payment = await db.ProgressPayments.SingleAsync(p => p.ProgressPaymentId == paymentId);
            await CreateService(db, manager).ApproveAsync(payment, note: null);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            var result = Assert.IsType<ViewResult>(
                await ApprovalController(db, new FakeCurrentUser()).Decide(token, "reject", null, "Olmaz."));
            Assert.Equal("NotPending", result.ViewName);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(ProgressPaymentStatus.APPROVED,
            (await verify.ProgressPayments.AsNoTracking().SingleAsync()).Status);
    }
}
