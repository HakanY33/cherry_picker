using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Entities;

namespace MipRental.Data.Services;

/// <summary>Token doğrulamasının sonucu. Her biri kullanıcıya AYRI bir sayfa gösterir.</summary>
public enum ApprovalTokenStatus
{
    /// <summary>Bulunamadı ya da biçimi geçersiz. İkisi AYNI cevabı verir: bilgi sızmasın.</summary>
    Invalid,
    Expired,
    Used,
    Revoked,
    Valid
}

public sealed record ApprovalTokenResult(ApprovalTokenStatus Status, ApprovalToken? Token);

/// <summary>
/// ADIM 14 BÖLÜM B — mail onayı token'ları (ADR-015, [[Mail ile Onay]]).
///
/// Token 32 bayt kriptografik rastgeledir ve VERİTABANINDA HAM HÂLİYLE DURMAZ:
/// yalnızca SHA-256 hash'i saklanır. Ham değer tek bir yerde görünür — kuyruğa
/// yazılan mailin gövdesindeki bağlantıda. Veritabanı sızsa bile o bağlantı
/// hash'ten geri üretilemez.
///
/// Arama hash üzerinden yapılır; karşılaştırma gizli değeri değil ZATEN HASH'İ
/// karşılaştırdığı için zamanlama saldırısı anlam taşımaz.
/// </summary>
public sealed class ApprovalTokenService
{
    /// <summary>7 gün (ADR-015). Eski mailden onay verilemez.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;

    public ApprovalTokenService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Hakediş + kullanıcı için token üretir. HAM token DÖNER (yalnızca burada
    /// görünür), veritabanına hash'i yazılır. SaveChanges ÇAĞIRILMAZ: token,
    /// durum değişikliği ve mail aynı transaction'a girsin.
    /// </summary>
    public string Issue(ProgressPayment payment, int issuedToUserId, DateTime nowUtc)
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        _db.ApprovalTokens.Add(new ApprovalToken
        {
            ProgressPayment = payment,
            ProgressPaymentId = payment.ProgressPaymentId,
            IssuedToUserId = issuedToUserId,
            TokenHash = Hash(raw),
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc.Add(Lifetime)
        });

        return raw;
    }

    /// <summary>
    /// Token'ı çözer. Hiçbir DURUM DEĞİŞİKLİĞİ YAPMAZ — GET yolundan da
    /// çağrılır (ADR-015'in en kritik kuralı).
    /// </summary>
    public async Task<ApprovalTokenResult> ResolveAsync(
        string? rawToken, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return new ApprovalTokenResult(ApprovalTokenStatus.Invalid, null);
        }

        var hash = Hash(rawToken);

        var token = await _db.ApprovalTokens
            .Include(t => t.ProgressPayment).ThenInclude(p => p.Period)
            .Include(t => t.ProgressPayment).ThenInclude(p => p.Firm)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            return new ApprovalTokenResult(ApprovalTokenStatus.Invalid, null);
        }

        // Sıra önemli: iptal edilmiş bir token'ın süresi de dolmuş olabilir;
        // kullanıcıya gösterilecek sebep en belirleyici olandır.
        if (token.UsedAt is not null)
        {
            return new ApprovalTokenResult(ApprovalTokenStatus.Used, token);
        }

        if (token.RevokedAt is not null)
        {
            return new ApprovalTokenResult(ApprovalTokenStatus.Revoked, token);
        }

        if (token.ExpiresAt <= nowUtc)
        {
            return new ApprovalTokenResult(ApprovalTokenStatus.Expired, token);
        }

        return new ApprovalTokenResult(ApprovalTokenStatus.Valid, token);
    }

    /// <summary>Kararla birlikte token tükenir; IP, tarayıcı ve zaman kaydedilir.</summary>
    public static void MarkUsed(ApprovalToken token, DateTime nowUtc, string? ip, string? userAgent)
    {
        token.UsedAt = nowUtc;
        token.UsedFromIp = ip;
        token.UsedUserAgent = Truncate(userAgent, 400);
    }

    /// <summary>
    /// B8 — hakedişin AÇIK tüm token'larını iptal eder. Geri çekilen hakedişin
    /// mail kutusunda duran bağlantısı derhal ölmelidir.
    /// </summary>
    public async Task<int> RevokeOpenTokensAsync(
        int progressPaymentId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var tokens = await _db.ApprovalTokens
            .Where(t => t.ProgressPaymentId == progressPaymentId && t.UsedAt == null && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = nowUtc;
        }

        return tokens.Count;
    }

    /// <summary>SHA-256. Ham token hiçbir yerde saklanmaz, yalnızca bu özet.</summary>
    public static byte[] Hash(string rawToken) => SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

    // URL'de görüneceği için Base64'ün URL-güvenli hâli: +/ yerine -_ ve dolgu yok.
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
