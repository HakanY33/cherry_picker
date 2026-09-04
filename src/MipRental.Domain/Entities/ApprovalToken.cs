namespace MipRental.Domain.Entities;

/// <summary>
/// Mail onayı için tek kullanımlık, süreli bağlantı token'ı (ADR-015).
///
/// B9 — POLİMORFİK BAĞ YOK. Diğer tablolarda kullanılan DocumentType +
/// DocumentId çifti burada BİLİNÇLİ OLARAK kullanılmadı: doğrudan
/// <see cref="ProgressPaymentId"/> FK'si var. Mail onayı sistemin genel bir
/// yeteneği değil, TEK BİR YERE kısıtlı bir istisnadır; genel bir çift, ileride
/// bir başka belgeye (çalışma kaydı, talep) oturumsuz onay açmayı bir satırlık
/// iş hâline getirirdi. Kısıt tipin kendisinde duruyor.
///
/// Ham token ASLA saklanmaz: veritabanında yalnızca SHA-256 hash'i durur
/// (<see cref="TokenHash"/>). Veritabanı sızsa bile bağlantı üretilemez.
/// </summary>
public class ApprovalToken
{
    public int ApprovalTokenId { get; set; }

    /// <summary>Token'ın bağlı olduğu TEK hakediş.</summary>
    public int ProgressPaymentId { get; set; }

    /// <summary>
    /// Bağlantının gönderildiği Bütçe Yöneticisi. Mailden gelen kararın AKTÖRÜ
    /// budur: oturum yok, kimliği token taşır. Karar bu kullanıcı adına yazılır.
    /// </summary>
    public int IssuedToUserId { get; set; }

    /// <summary>Ham token'ın SHA-256 hash'i (32 bayt). Benzersizdir.</summary>
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; }

    /// <summary>7 gün. Eski mailden onay verilemez.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Doluysa token tükenmiştir; ikinci kez çalışmaz.</summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>Denetim izi: kararın nereden ve hangi tarayıcıdan geldiği.</summary>
    public string? UsedFromIp { get; set; }
    public string? UsedUserAgent { get; set; }

    /// <summary>
    /// B8 — hakediş geri çekilirse token DERHAL iptal olur. Aksi halde mail
    /// kutusundaki eski bağlantı çalışmaya devam eder ve geri çekilmiş bir
    /// hakediş onaylanabilirdi.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    public ProgressPayment ProgressPayment { get; set; } = null!;
    public User IssuedToUser { get; set; } = null!;

    public bool IsSpent(DateTime nowUtc) => UsedAt is not null || RevokedAt is not null || ExpiresAt <= nowUtc;
}
