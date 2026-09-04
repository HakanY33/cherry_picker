using Microsoft.Extensions.Logging;
using MipRental.Domain.Abstractions;

namespace MipRental.Tests;

/// <summary>
/// Testlerde gerçek SMTP'ye BAĞLANILMAZ. Bu sahte gönderici ne gittiğini
/// kaydeder ve istenirse hata fırlatır.
/// </summary>
internal sealed class FakeEmailSender : IEmailSender
{
    public bool IsEnabled { get; set; } = true;

    /// <summary>Doluysa her gönderim bu hatayla başarısız olur.</summary>
    public string? FailWith { get; set; }

    public List<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            throw new InvalidOperationException(FailWith);
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Log satırlarını biriktirir: "şu değer loglanmadı" testleri için.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Lines.Add(formatter(state, exception));
        if (exception is not null)
        {
            Lines.Add(exception.ToString());
        }
    }

    public string All => string.Join("\n", Lines);
}
