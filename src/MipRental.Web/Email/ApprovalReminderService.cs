using MipRental.Data.Email;
using MipRental.Domain.Abstractions;

namespace MipRental.Web.Email;

/// <summary>
/// Hatırlatma ve eskalasyon zamanlayıcısı (CLAUDE.md kural 5).
///
/// MAİL AYARINDAN BAĞIMSIZ çalışır: bildirim üretmek gönderimden ayrı bir iştir.
/// SMTP kapalıyken de hatırlatma kuyruğa yazılır ve uygulama içinde görünür.
///
/// Aralık kuyruk işleyiciyle aynı ayardan okunur; hatırlatma için saatlik bile
/// yeterdi ama ikinci bir ayar açmak MIP'e verilecek listeyi uzatırdı.
/// </summary>
public sealed class ApprovalReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailOptions _options;
    private readonly ILogger<ApprovalReminderService> _logger;

    public ApprovalReminderService(
        IServiceScopeFactory scopeFactory, EmailOptions options, ILogger<ApprovalReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, _options.QueueIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scheduler = scope.ServiceProvider.GetRequiredService<ApprovalReminderScheduler>();
                var queued = await scheduler.RunAsync(DateTime.UtcNow, stoppingToken);

                if (queued > 0)
                {
                    _logger.LogInformation("{Count} hatırlatma/eskalasyon bildirimi üretildi.", queued);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hatırlatma turu başarısız oldu.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
