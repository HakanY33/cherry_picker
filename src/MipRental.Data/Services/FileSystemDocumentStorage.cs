using System.Globalization;

namespace MipRental.Data.Services;

/// <summary>
/// Belgeleri dosya sisteminde saklar: {kök}/{yyyy}/{MM}/{zaman}-{dosyaadı}.
///
/// Ay bazlı klasörleme, yıllar içinde tek klasörde on binlerce dosya birikmesini
/// engeller. Dosya adının başındaki zaman damgası, aynı belgenin YENİDEN üretilen
/// sürümlerinin birbirini EZMEMESİNİ garanti eder — eski sürüm diskte kalır
/// (bkz. GeneratedDocumentService).
/// </summary>
public sealed class FileSystemDocumentStorage : IDocumentStorage
{
    private readonly string _rootPath;

    public FileSystemDocumentStorage(string rootPath)
    {
        _rootPath = rootPath;
    }

    public async Task<string> SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var relativeDirectory = Path.Combine(
            now.Year.ToString(CultureInfo.InvariantCulture),
            now.Month.ToString("00", CultureInfo.InvariantCulture));

        var absoluteDirectory = Path.Combine(_rootPath, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var safeName = $"{now:yyyyMMddHHmmssfff}-{Sanitize(fileName)}";
        await File.WriteAllBytesAsync(Path.Combine(absoluteDirectory, safeName), content, cancellationToken);

        // Kök dizin ortamdan ortama değişebilir (dev/test/prod); veritabanına
        // mutlak yol değil, köke GÖRE yol yazılır.
        return Path.Combine(relativeDirectory, safeName).Replace('\\', '/');
    }

    /// <summary>
    /// Dosya adındaki geçersiz karakterleri temizler. Belge numarası ve firma adı
    /// dosya adına giriyor; Türkçe karakter korunur, yol ayıracı korunmaz.
    /// </summary>
    private static string Sanitize(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "belge" : cleaned;
    }
}
