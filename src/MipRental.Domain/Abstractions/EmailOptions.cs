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

    /// <summary>Kuyruk işleyicinin çalışma aralığı (saniye).</summary>
    public int QueueIntervalSeconds { get; set; } = 60;

    /// <summary>Bir bildirim için azami deneme sayısı.</summary>
    public int MaxRetryCount { get; set; } = 5;

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
