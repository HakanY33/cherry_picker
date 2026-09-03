namespace MipRental.Domain.Enums;

/// <summary>
/// Talep durumlarının Türkçe karşılıkları. İKİ ayrı sözlük vardır ve bu
/// bilinçlidir:
///
/// <see cref="Labels"/>  — GERÇEK durum. Süreci yürütenler (Ekipman Müdürlüğü,
///                          firma yetkilisi, operatör) bunu görür.
/// <see cref="Summary"/> — SADELEŞTİRİLMİŞ durum. Talebi açan kişi bunu görür:
///                          onun için "Ekipman onayı bekliyor" ile "Firma onayı
///                          bekliyor" arasındaki fark eylem gerektirmez, ikisi
///                          de "Bekliyor"dur. Süreci ilgilendirmeyen kişiye iç
///                          işleyişi göstermek gereksiz bilgi ve gereksiz soru
///                          üretir.
///
/// Domain'deki hata mesajları da Web'deki ekran etiketleri de buradan okur.
/// </summary>
public static class RequestStatusLabels
{
    public static readonly IReadOnlyDictionary<RequestStatus, string> Labels = new Dictionary<RequestStatus, string>
    {
        [RequestStatus.DRAFT] = "Taslak",
        [RequestStatus.SUBMITTED] = "Gönderildi",
        [RequestStatus.PENDING_EQUIPMENT] = "Ekipman Müdürlüğü Onayı Bekliyor",
        [RequestStatus.PENDING_FIRM] = "Firma Onayı Bekliyor",
        [RequestStatus.SCHEDULED] = "Planlandı",
        [RequestStatus.IN_PROGRESS] = "Devam Ediyor",
        [RequestStatus.COMPLETED] = "Tamamlandı",
        [RequestStatus.REJECTED_BY_EQUIPMENT] = "Ekipman Müdürlüğü Reddetti",
        [RequestStatus.REJECTED_BY_FIRM] = "Firma Reddetti",
        [RequestStatus.CANCELLED] = "İptal Edildi"
    };

    /// <summary>
    /// Talebi açana gösterilen sadeleştirilmiş etiket.
    ///
    /// DRAFT bilinçli olarak "Taslak" kalır: henüz gönderilmemiş bir talebi
    /// "Bekliyor" diye göstermek, talep açanı bekleyecek bir şey olduğuna
    /// inandırır — oysa top hâlâ ondadır.
    /// </summary>
    public static readonly IReadOnlyDictionary<RequestStatus, string> Summary = new Dictionary<RequestStatus, string>
    {
        [RequestStatus.DRAFT] = "Taslak",
        [RequestStatus.SUBMITTED] = "Bekliyor",
        [RequestStatus.PENDING_EQUIPMENT] = "Bekliyor",
        [RequestStatus.PENDING_FIRM] = "Bekliyor",
        [RequestStatus.SCHEDULED] = "Onaylandı",
        [RequestStatus.IN_PROGRESS] = "Onaylandı",
        [RequestStatus.COMPLETED] = "Tamamlandı",
        [RequestStatus.REJECTED_BY_EQUIPMENT] = "Reddedildi",
        [RequestStatus.REJECTED_BY_FIRM] = "Reddedildi",
        [RequestStatus.CANCELLED] = "İptal edildi"
    };

    public static string Get(RequestStatus status) =>
        Labels.TryGetValue(status, out var label) ? label : status.ToString();

    /// <summary>Talebi açana gösterilecek sade etiket.</summary>
    public static string GetSummary(RequestStatus status) =>
        Summary.TryGetValue(status, out var label) ? label : status.ToString();
}
