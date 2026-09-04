using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Email;
using MipRental.Data.Interceptors;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Tests;

/// <summary>
/// ADIM 15 — MAİL GÖNDERİM ALTYAPISI.
///
/// Hiçbir test gerçek SMTP'ye bağlanmaz: <see cref="FakeEmailSender"/> kullanılır.
/// Testlerin duruşu diğer adımlarla aynı — "servis şunu döndü" değil,
/// VERİTABANINDAKİ satırın durumu, deneme sayısı ve hata mesajı doğrulanır.
/// </summary>
public class EmailDispatchTests
{
    private const string InternalFrom = "miprental@mip.com.tr";

    private static AppDbContext CreateContext(SqliteConnection connection) =>
        new SqliteTestContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
                .Options,
            new FakeCurrentUser());

    private static async Task<SqliteConnection> CreateConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        return connection;
    }

    private static EmailOptions Options(
        bool enabled = true, string? testMode = null, bool allowExternal = true, int maxRetry = 5) =>
        new()
        {
            Enabled = enabled,
            Host = "smtp.test",
            Port = 587,
            FromAddress = InternalFrom,
            AllowExternalRecipients = allowExternal,
            TestModeRecipient = testMode ?? string.Empty,
            MaxRetryCount = maxRetry
        };

    private static async Task<long> QueueAsync(
        SqliteConnection connection, string email, string template = "WR_APPROVAL_PENDING", string body = "Deneme gövdesi.")
    {
        await using var db = CreateContext(connection);
        var notification = new Notification
        {
            Email = email,
            Channel = NotificationChannel.EMAIL,
            TemplateCode = template,
            Subject = "Konu",
            Body = body,
            Status = NotificationStatus.QUEUED,
            CreatedAt = DateTime.UtcNow
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification.NotificationId;
    }

    private static NotificationDispatcher CreateDispatcher(
        AppDbContext db, IEmailSender sender, EmailOptions options, RecordingLogger<NotificationDispatcher>? logger = null) =>
        new(db, sender, options, logger ?? new RecordingLogger<NotificationDispatcher>());

    // ---------------------------------------------------------------
    // 1) SMTP ayarı yokken uygulama çalışır, bildirim kuyrukta kalır
    // ---------------------------------------------------------------

    [Fact]
    public async Task WithoutSmtpConfiguration_NotificationStaysQueued()
    {
        await using var connection = await CreateConnectionAsync();
        var id = await QueueAsync(connection, "birisi@mip.com.tr");

        var sender = new FakeEmailSender { IsEnabled = false };   // NoOp karşılığı
        await using (var db = CreateContext(connection))
        {
            var processed = await CreateDispatcher(db, sender, Options(enabled: false))
                .DispatchQueuedAsync(DateTime.UtcNow);

            Assert.Equal(0, processed);
        }

        await using var verify = CreateContext(connection);
        var stored = await verify.Notifications.AsNoTracking().SingleAsync(n => n.NotificationId == id);

        Assert.Equal(NotificationStatus.QUEUED, stored.Status);
        Assert.Equal(0, stored.RetryCount);
        Assert.Null(stored.SentAt);
        Assert.Empty(sender.Sent);
    }

    // ---------------------------------------------------------------
    // 2) Test modu: TÜM mailler tek adrese
    // ---------------------------------------------------------------

    [Fact]
    public async Task TestModeRecipient_RedirectsEveryMail()
    {
        await using var connection = await CreateConnectionAsync();
        await QueueAsync(connection, "gercek1@mip.com.tr");
        await QueueAsync(connection, "gercek2@altyuklenici.com");

        var sender = new FakeEmailSender();
        await using (var db = CreateContext(connection))
        {
            await CreateDispatcher(db, sender, Options(testMode: "deneme@mip.com.tr"))
                .DispatchQueuedAsync(DateTime.UtcNow);
        }

        Assert.Equal(2, sender.Sent.Count);
        Assert.All(sender.Sent, m => Assert.Equal("deneme@mip.com.tr", m.To));

        // Gerçek alıcı kayıtta OLDUĞU GİBİ kalır; yalnızca teslim adresi değişti.
        await using var verify = CreateContext(connection);
        var adresler = await verify.Notifications.AsNoTracking().Select(n => n.Email).ToListAsync();
        Assert.Contains("gercek1@mip.com.tr", adresler);
        Assert.Contains("gercek2@altyuklenici.com", adresler);
    }

    // ---------------------------------------------------------------
    // 3) Dış alıcı politikası kapalı: SKIPPED_EXTERNAL
    // ---------------------------------------------------------------

    [Fact]
    public async Task ExternalRecipientsDisabled_MarksSkippedExternal()
    {
        await using var connection = await CreateConnectionAsync();
        var icerideki = await QueueAsync(connection, "ic@mip.com.tr");
        var disaridaki = await QueueAsync(connection, "dis@altyuklenici.com");

        var sender = new FakeEmailSender();
        await using (var db = CreateContext(connection))
        {
            await CreateDispatcher(db, sender, Options(allowExternal: false)).DispatchQueuedAsync(DateTime.UtcNow);
        }

        await using var verify = CreateContext(connection);
        var ic = await verify.Notifications.AsNoTracking().SingleAsync(n => n.NotificationId == icerideki);
        var dis = await verify.Notifications.AsNoTracking().SingleAsync(n => n.NotificationId == disaridaki);

        Assert.Equal(NotificationStatus.SENT, ic.Status);
        Assert.Equal(NotificationStatus.SKIPPED_EXTERNAL, dis.Status);

        // Atlanan bildirim HATA DEĞİLDİR: deneme sayısı artmaz, hata mesajı yoktur.
        Assert.Equal(0, dis.RetryCount);
        Assert.Null(dis.LastError);
        Assert.Equal("ic@mip.com.tr", Assert.Single(sender.Sent).To);
    }

    // ---------------------------------------------------------------
    // 4) Başarısız gönderim: deneme sayısı artar, 5'te durur
    // ---------------------------------------------------------------

    [Fact]
    public async Task FailedSend_IncrementsRetry_AndStopsAtMax()
    {
        await using var connection = await CreateConnectionAsync();
        var id = await QueueAsync(connection, "birisi@mip.com.tr");

        var sender = new FakeEmailSender { FailWith = "SMTP sunucusuna ulaşılamadı" };
        var now = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);

        for (var attempt = 1; attempt <= 7; attempt++)
        {
            await using var db = CreateContext(connection);
            // Geri çekilme süresi dolmuş sayılsın diye saat ileri alınır.
            await CreateDispatcher(db, sender, Options()).DispatchQueuedAsync(now.AddHours(attempt));
        }

        await using var verify = CreateContext(connection);
        var stored = await verify.Notifications.AsNoTracking().SingleAsync(n => n.NotificationId == id);

        Assert.Equal(NotificationStatus.FAILED, stored.Status);
        Assert.Equal(5, stored.RetryCount);                       // 5'te durdu, 7'ye çıkmadı
        Assert.Contains("ulaşılamadı", stored.LastError);
        Assert.Null(stored.NextAttemptAt);                        // artık denenmiyor
        Assert.Empty(sender.Sent);
    }

    /// <summary>Geri çekilme: başarısızlıktan hemen sonra tekrar denenmez.</summary>
    [Fact]
    public async Task FailedSend_WaitsForBackoffBeforeRetrying()
    {
        await using var connection = await CreateConnectionAsync();
        var id = await QueueAsync(connection, "birisi@mip.com.tr");

        var sender = new FakeEmailSender { FailWith = "geçici hata" };
        var now = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);

        await using (var db = CreateContext(connection))
        {
            await CreateDispatcher(db, sender, Options()).DispatchQueuedAsync(now);
        }

        await using (var db = CreateContext(connection))
        {
            // Aynı dakika içinde ikinci tur: sıra bu satıra gelmez.
            var processed = await CreateDispatcher(db, sender, Options()).DispatchQueuedAsync(now.AddSeconds(30));
            Assert.Equal(0, processed);
        }

        await using var verify = CreateContext(connection);
        var stored = await verify.Notifications.AsNoTracking().SingleAsync(n => n.NotificationId == id);
        Assert.Equal(1, stored.RetryCount);
        Assert.Equal(now.AddMinutes(1), stored.NextAttemptAt);
    }

    // ---------------------------------------------------------------
    // 5) Aynı bildirim iki kez gönderilmez
    // ---------------------------------------------------------------

    /// <summary>
    /// İkinci tur aynı satırı bulmaz: satır ilk turda SENT olmuştur. Kayıt
    /// kilidi (QUEUED -> SENDING atomik güncellemesi) bunu garanti eder.
    /// </summary>
    [Fact]
    public async Task SameNotification_IsNotSentTwice()
    {
        await using var connection = await CreateConnectionAsync();
        await QueueAsync(connection, "birisi@mip.com.tr");

        var sender = new FakeEmailSender();
        var now = DateTime.UtcNow;

        for (var tur = 0; tur < 3; tur++)
        {
            await using var db = CreateContext(connection);
            await CreateDispatcher(db, sender, Options()).DispatchQueuedAsync(now);
        }

        Assert.Single(sender.Sent);

        await using var verify = CreateContext(connection);
        Assert.Equal(1, await verify.Notifications.CountAsync(n => n.Status == NotificationStatus.SENT));
    }

    /// <summary>SENDING durumundaki satırı ikinci bir işleyici üstlenemez.</summary>
    [Fact]
    public async Task NotificationBeingSent_IsNotPickedUpAgain()
    {
        await using var connection = await CreateConnectionAsync();
        var id = await QueueAsync(connection, "birisi@mip.com.tr");

        await using (var db = CreateContext(connection))
        {
            var n = await db.Notifications.SingleAsync(x => x.NotificationId == id);
            n.Status = NotificationStatus.SENDING;      // başka bir işleyici üstlendi
            await db.SaveChangesAsync();
        }

        var sender = new FakeEmailSender();
        await using (var db = CreateContext(connection))
        {
            var processed = await CreateDispatcher(db, sender, Options()).DispatchQueuedAsync(DateTime.UtcNow);
            Assert.Equal(0, processed);
        }

        Assert.Empty(sender.Sent);
    }

    // ---------------------------------------------------------------
    // 8) Magic link token'ı loglanmaz
    // ---------------------------------------------------------------

    /// <summary>
    /// Hakediş onay maili gövdesinde HAM TOKEN taşır. Gönderim başarısız olsa
    /// bile token hiçbir log satırına düşmemeli — hata mesajı, gövde, konu.
    /// </summary>
    [Fact]
    public async Task MagicLinkToken_IsNeverLogged()
    {
        const string token = "rIYFZpasyG3wkQIkgLyG8A4gaN0AP9wqhMDKVtXIE";

        await using var connection = await CreateConnectionAsync();
        await QueueAsync(connection, "yonetici@mip.com.tr", "PP_APPROVAL_LINK",
            $"Hakediş onayınızı bekliyor. Bağlantı: https://mip.test/Onay/{token}");

        var logger = new RecordingLogger<NotificationDispatcher>();
        var sender = new FakeEmailSender { FailWith = "SMTP reddetti" };

        await using (var db = CreateContext(connection))
        {
            await CreateDispatcher(db, sender, Options(), logger).DispatchQueuedAsync(DateTime.UtcNow);
        }

        Assert.NotEmpty(logger.Lines);                      // gerçekten loglandı
        Assert.DoesNotContain(token, logger.All);           // ama token yok
        Assert.DoesNotContain("Onay/", logger.All);

        // Veritabanındaki hata alanına da gövde yazılmaz.
        await using var verify = CreateContext(connection);
        var stored = await verify.Notifications.AsNoTracking().SingleAsync();
        Assert.DoesNotContain(token, stored.LastError ?? string.Empty);
    }

    /// <summary>Gövde hassassa gönderici de içeriği loglamaz — işaret taşınıyor mu?</summary>
    [Fact]
    public async Task MagicLinkMail_IsMarkedAsSecret()
    {
        await using var connection = await CreateConnectionAsync();
        await QueueAsync(connection, "yonetici@mip.com.tr", "PP_APPROVAL_LINK", "Bağlantı: https://mip.test/Onay/abc");
        await QueueAsync(connection, "birisi@mip.com.tr", "WR_APPROVAL_PENDING", "Onayınızı bekliyor.");

        var sender = new FakeEmailSender();
        await using (var db = CreateContext(connection))
        {
            await CreateDispatcher(db, sender, Options()).DispatchQueuedAsync(DateTime.UtcNow);
        }

        var magic = sender.Sent.Single(m => m.HtmlBody.Contains("Onay/abc"));
        var digeri = sender.Sent.Single(m => !m.HtmlBody.Contains("Onay/abc"));

        Assert.True(magic.ContainsSecret);
        Assert.False(digeri.ContainsSecret);
    }

    // ---------------------------------------------------------------
    // 9) Mail gövdesinde tutar geçmez (hakediş hariç)
    // ---------------------------------------------------------------

    /// <summary>
    /// Onay bildiriminin alıcısı adımın rolündeki kişidir (ör. EQUIPMENT_MANAGER)
    /// ve o rol fiyat GÖRMEZ. Gövdede tutar olsaydı onaylama yetkisi sessizce
    /// fiyat görme yetkisine dönüşürdü (ADR-016).
    /// </summary>
    [Fact]
    public void ApprovalMailBody_ContainsNoAmount()
    {
        var kaynak = File.ReadAllText(Path.Combine(RepoRoot(), "src/MipRental.Data/Services/NotificationQueue.cs"));

        // Onay bekliyor bildiriminin gövdesi
        var baslangic = kaynak.IndexOf("QueueApprovalPendingAsync", StringComparison.Ordinal);
        var bitis = kaynak.IndexOf("QueueWorkRecordDecisionAsync", baslangic, StringComparison.Ordinal);
        var bolum = bitis > baslangic ? kaynak[baslangic..bitis] : kaynak[baslangic..];

        Assert.DoesNotContain("TotalAmount", bolum);
        Assert.DoesNotContain("MobilizationFee", bolum);
    }

    /// <summary>Tutar taşımasına izin verilen TEK şablon hakediş onay mailidir.</summary>
    [Fact]
    public void OnlyProgressPaymentTemplate_MayContainAmount()
    {
        Assert.True(EmailTemplates.MayContainAmount("PP_APPROVAL_LINK"));

        foreach (var template in new[]
        {
            "WR_APPROVAL_PENDING", "WR_APPROVAL_REMINDER", "WR_APPROVAL_ESCALATION",
            "WR_APPROVED", "WR_REJECTED", "WR_REVISION_REQUESTED",
            "REQ_SUBMITTED", "REQ_FIRM_ACCEPTED", "WR_DERIVED_PENDING_SUBMIT"
        })
        {
            Assert.False(EmailTemplates.MayContainAmount(template));
        }
    }

    /// <summary>Şablon kullanıcı girdisini HTML olarak yorumlamaz.</summary>
    [Fact]
    public void Template_EscapesUserContent()
    {
        var html = EmailTemplates.Render("WR_REJECTED", "Konu", "<script>alert(1)</script> & gerekçe");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Bu e-posta otomatik gönderilmiştir", html);
        Assert.Contains("yanıtlanmaz", html);
    }

    /// <summary>Gövdedeki bağlantı tıklanabilir olur (magic link bunun için).</summary>
    [Fact]
    public void Template_MakesLinkClickable()
    {
        var html = EmailTemplates.Render("PP_APPROVAL_LINK", "Hakediş", "Bağlantı: https://mip.test/Onay/abc123");

        Assert.Contains("<a href=\"https://mip.test/Onay/abc123\"", html);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }
}
