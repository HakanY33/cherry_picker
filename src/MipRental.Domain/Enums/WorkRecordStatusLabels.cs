namespace MipRental.Domain.Enums;

// Durum adlarının TEK Türkçe kaynağı. Domain katmanındaki hata mesajları da,
// Web katmanındaki ekran etiketleri de buradan okur — iki ayrı sözlük tutulmaz.
public static class WorkRecordStatusLabels
{
    public static readonly IReadOnlyDictionary<WorkRecordStatus, string> Labels = new Dictionary<WorkRecordStatus, string>
    {
        [WorkRecordStatus.DRAFT] = "Taslak",
        [WorkRecordStatus.SUBMITTED] = "Gönderildi",
        [WorkRecordStatus.PENDING] = "Onay Bekliyor",
        [WorkRecordStatus.APPROVED] = "Onaylandı",
        [WorkRecordStatus.REJECTED] = "Reddedildi",
        [WorkRecordStatus.REVISION_REQUESTED] = "Revizyon İstendi",
        [WorkRecordStatus.CANCELLED] = "İptal Edildi",
        [WorkRecordStatus.LOCKED] = "Kilitli"
    };

    public static string Get(WorkRecordStatus status) => Labels.TryGetValue(status, out var label) ? label : status.ToString();
}
