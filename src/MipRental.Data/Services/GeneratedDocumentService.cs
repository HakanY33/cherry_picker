using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Services;

/// <summary>
/// Üretilen belgeyi diske yazar ve GeneratedDocuments'a kaydeder.
///
/// Kurallar:
/// - Belge YENİDEN üretilirse eski kayıt SİLİNMEZ, yeni bir SÜRÜM eklenir. Eldeki
///   kâğıdın doğrulama kodu, yenisi üretildikten sonra da çalışmaya devam eder.
/// - VerificationCode Guid tabanlıdır. Artan sayı KULLANILMAZ: sıradaki belgenin
///   kodunu tahmin edip başkasının belgesini sorgulamak mümkün olmamalı.
/// - Hash SHA-256'dır ve dosyanın BAYTLARINDAN hesaplanır. Aynı belgenin iki
///   üretimi genelde farklı hash verir (PDF'in içinde üretim zamanı vardır);
///   hash'in işi iki üretimi eşitlemek değil, TEK bir dosyanın sonradan
///   değiştirilmediğini kanıtlamaktır.
/// </summary>
public sealed class GeneratedDocumentService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDocumentStorage _storage;

    public GeneratedDocumentService(AppDbContext db, ICurrentUser currentUser, IDocumentStorage storage)
    {
        _db = db;
        _currentUser = currentUser;
        _storage = storage;
    }

    /// <summary>
    /// Belgeyi arşivler ve oluşan GeneratedDocument kaydını döner. SaveChanges ÇAĞRILIR:
    /// dosya diske yazıldıktan sonra kaydın da atılmış olması gerekir, yoksa diskte
    /// kaydı olmayan bir dosya kalır.
    /// </summary>
    public async Task<GeneratedDocument> ArchiveAsync(
        GeneratedDocumentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Kod, belgenin İÇİNE (karekod + okunabilir metin) basıldığı için PDF
        // üretilmeden ÖNCE çağıran tarafından NewVerificationCode() ile alınır ve
        // buraya aynen taşınır. Burada yeniden üretmek, kâğıttaki kod ile
        // veritabanındaki kodun ayrışması demek olurdu.
        var storagePath = await _storage.SaveAsync(request.FileName, request.Content, cancellationToken);

        var document = new GeneratedDocument
        {
            DocumentType = request.DocumentType,
            DocumentId = request.DocumentId,
            Kind = request.Kind,
            FirmId = request.FirmId,
            FileName = request.FileName,
            StoragePath = storagePath,
            ContentHash = ComputeHash(request.Content),
            VerificationCode = request.VerificationCode,
            TemplateVersion = request.TemplateVersion,
            TotalAmount = request.TotalAmount,
            Currency = request.Currency,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = _currentUser.UserId > 0 ? _currentUser.UserId : null
        };

        _db.GeneratedDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);
        return document;
    }

    /// <summary>
    /// Doğrulama kodundan belgeyi bulur. Firma izolasyon filtresi BİLİNÇLİ olarak
    /// atlanır: /Dogrula/{kod} açık bir sayfadır ve oturum açmamış birinin de
    /// çalışması gerekir. Sayfanın kişisel veri göstermemesi bu yüzden şart
    /// (bkz. VerificationController).
    /// </summary>
    public Task<GeneratedDocument?> FindByVerificationCodeAsync(string code, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(code)
            ? Task.FromResult<GeneratedDocument?>(null)
            : _db.GeneratedDocuments.IgnoreQueryFilters().AsNoTracking()
                .Include(d => d.Firm)
                .FirstOrDefaultAsync(d => d.VerificationCode == code, cancellationToken);

    /// <summary>
    /// Guid tabanlı, tahmin edilemez doğrulama kodu. 32 karakter büyük harf hex —
    /// GeneratedDocuments.VerificationCode nvarchar(40)'a sığar.
    /// </summary>
    public static string NewVerificationCode() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    public static string ComputeHash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToUpperInvariant();
}

/// <summary>Arşivlenecek belgenin tarifi.</summary>
public sealed class GeneratedDocumentRequest
{
    public required DocumentType DocumentType { get; init; }
    public required int DocumentId { get; init; }
    public required GeneratedDocumentKind Kind { get; init; }
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }

    /// <summary>
    /// Belgenin üzerine basılmış doğrulama kodu. GeneratedDocumentService.NewVerificationCode()
    /// ile PDF üretilmeden önce alınır; kâğıttaki kod ile kayıttaki kod aynı olmak zorunda.
    /// </summary>
    public required string VerificationCode { get; init; }

    public int? FirmId { get; init; }
    public string? TemplateVersion { get; init; }

    /// <summary>Belgenin üzerinde yazan tutar; doğrulama sayfası bunu gösterir.</summary>
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
}

/// <summary>
/// Üretilen belgelerin dosya deposu. Arayüz olmasının sebebi testte diske
/// yazmamaktır; üretimde tek gerçeklemesi FileSystemDocumentStorage'dır.
/// </summary>
public interface IDocumentStorage
{
    /// <summary>Dosyayı yazar ve kaydedilecek göreli yolu döner.</summary>
    Task<string> SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken = default);
}
