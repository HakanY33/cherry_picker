namespace MipRental.Domain.Abstractions;

/// <summary>
/// Mail yapılandırması. appsettings'teki "Email" bölümünden bağlanır; değerleri
/// MIP verir (bkz. docs/EMAIL-SETUP.md).
///
/// ŞİFRE appsettings'e YAZILMAZ: geliştirmede user-secrets, canlıda ortam
/// değişkeni / IIS uygulama ayarı ile gelir.
///
/// Domain'de durmasının sebebi: kuyruk işleyicisi (Data) de test modu ve dış
/// alıcı politikasını uygulamak zorunda; Web'e bağımlı olamaz.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Kapalıysa hiçbir şey gönderilmez; bildirimler kuyrukta bekler.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "MIP Hizmet Kiralama";

    /// <summary>
    /// false ise MIP alan adı DIŞINDAKİ alıcılara mail gönderilmez; o bildirimler
    /// SKIPPED_EXTERNAL olarak işaretlenir ve yalnızca uygulama içinde görünür.
    /// Kurumsal politika dışarı mail çıkışına izin vermeyebilir.
    /// </summary>
    public bool AllowExternalRecipients { get; set; } = true;

    /// <summary>
    /// Doluysa TÜM mailler bu adrese gider, gerçek alıcıya değil. Devreye alma
    /// sırasında güvenli deneme içindir.
    /// </summary>
    public string TestModeRecipient { get; set; } = string.Empty;

    /// <summary>
    /// Uygulamanın dış adresi ("https://miprental.mip.com.tr"). Mail gövdesinin
    /// altına "uygulamayı aç" bağlantısı olarak eklenir.
    ///
    /// BU BİR MAGIC LINK DEĞİLDİR ve olmayacaktır: normal giriş isteyen adres.
    /// Oturumsuz karar verme yalnızca hakediş onayına açıktır (ADR-030) ve o
    /// bağlantı kendi mail gövdesinde üretilir. Boşsa bağlantı satırı hiç
    /// yazılmaz — yanlış bir adrese yönlendirmektense yönlendirmemek yeğdir.
    /// </summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// ADIM 16 B5 — "onayınız bekliyor" tipi bildirimlerin ÖZETLENME aralığı
    /// (saat). 0 = anlık (VARSAYILAN, bugünkü davranış).
    ///
    /// Açıkken aynı kullanıcıya aynı tipteki bildirimler bu süre boyunca
    /// biriktirilir ve TEK mailde gider: 20 talebin onayını bekleyen kişi 20
    /// mail almaz. Red, itiraz, eskalasyon ve magic link ASLA özetlenmez —
    /// bunlar gecikmesi maliyetli olan haberlerdir.
    /// </summary>
    public int DigestHours { get; set; }

    /// <summary>Kuyruk işleyicinin çalışma aralığı (saniye).</summary>
    public int QueueIntervalSeconds { get; set; } = 60;

    /// <summary>Bir bildirim için azami deneme sayısı.</summary>
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>
    /// ADIM 16 — süre teyidi gelmezse kaç saat sonra talebi açana HATIRLATILIR.
    /// 0 = hatırlatma yok. OTOMATİK TEYİT HİÇBİR DEĞERDE OLMAZ (kural 5);
    /// bu ayar yalnızca insanın ne zaman dürtüleceğini söyler.
    ///
    /// Neden ApprovalFlowSteps'te değil: kural 6 ONAY ZİNCİRİ içindir. Süre
    /// teyidi bir onay adımı değil, tarafları sabit tek bir el değiştirmedir
    /// (bkz. RequestStateMachine); zincir tablosuna satır açmak, olmayan bir
    /// zinciri varmış gibi gösterirdi.
    /// </summary>
    public int ConfirmationReminderHours { get; set; } = 24;

    /// <summary>
    /// Süre teyidi gelmezse kaç saat sonra Ekipman Müdürlüğü'ne ESKALE edilir.
    /// 0 = eskalasyon yok.
    /// </summary>
    public int ConfirmationEscalationHours { get; set; } = 72;

    /// <summary>
    /// "İç alıcı" sayılan alan adı. Ayrıca verilmez: gönderen adresinden türer
    /// (noreply@mip.com.tr -> mip.com.tr). İki ayrı doğruluk kaynağı olmasın.
    /// </summary>
    public string InternalDomain =>
        FromAddress.Contains('@') ? FromAddress[(FromAddress.LastIndexOf('@') + 1)..].Trim().ToLowerInvariant() : string.Empty;

    /// <summary>Gerçekten gönderim yapılabilir mi? Eksik ayar = NoOp.</summary>
    public bool IsUsable =>
        Enabled && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}
