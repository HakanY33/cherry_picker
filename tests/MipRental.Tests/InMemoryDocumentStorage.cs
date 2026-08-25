using MipRental.Data.Services;

namespace MipRental.Tests;

/// <summary>
/// Testlerde diske yazmayan belge deposu. Yazılan dosyaları bellekte tutar, böylece
/// test "aynı PDF iki kez üretilince iki ayrı dosya oluştu mu" gibi şeyleri
/// doğrulayabilir.
/// </summary>
internal sealed class InMemoryDocumentStorage : IDocumentStorage
{
    private int _counter;

    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public Task<string> SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        // Üretimdeki FileSystemDocumentStorage gibi: her çağrı YENİ bir yol üretir,
        // önceki sürümün üzerine yazılmaz.
        var path = $"test/{Interlocked.Increment(ref _counter):00000}-{fileName}";
        Files[path] = content;
        return Task.FromResult(path);
    }
}
