using MipRental.Data.Email;
using MipRental.Domain.Abstractions;

namespace MipRental.Web.Email;

/// <summary>
/// Kuyruk işleyicinin ZAMANLAYICISI. İş NotificationDispatcher'da; burada
/// yalnızca "her X saniyede bir çalıştır" vardır.
///
/// Hiçbir hata uygulamayı düşürmez: tur içindeki istisna yakalanır, loglanır ve
/// bir sonraki tur normal devam eder. Mail sunucusu çökse de sistem ayakta kalır.
/// </summary>
public sealed class NotificationSenderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailOptions _options;
    private readonly ILogger<NotificationSenderService> _logger;

    public NotificationSenderService(
        IServiceScopeFactory scopeFactory, EmailOptions options, ILogger<NotificationSenderService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(10, _options.QueueIntervalSeconds));

        if (!_options.IsUsable)
        {
            // Ayar yoksa servis boşuna dönmez; bildirimler kuyrukta bekler ve
            // uygulama içinde görünmeye devam eder.
            _logger.LogInformation(
                "Mail yapılandırması yok/kapalı: kuyruk işleyici beklemede, bildirimler QUEUED kalacak.");
            return;
        }

        _logger.LogInformation("Kuyruk işleyici başladı. Aralık: {Seconds} sn.", interval.TotalSeconds);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                var processed = await dispatcher.DispatchQueuedAsync(DateTime.UtcNow, stoppingToken);

                if (processed > 0)
                {
                    _logger.LogInformation("{Count} bildirim işlendi.", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Tur başarısız olabilir (veritabanı, ağ); sıradaki tur devam eder.
                _logger.LogError(ex, "Kuyruk işleyici turu başarısız oldu.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
