namespace MipRental.Domain.Entities;

/// <summary>
/// Hakedişin DONDURULMUŞ kayıt listesi: hangi çalışma kayıtları bu hakedişe
/// girdi. Liste sonradan sorguyla yeniden kurulmaz — hakediş oluştuktan sonra
/// aynı döneme yeni bir kayıt onaylanırsa sorgu farklı sonuç verirdi.
///
/// Tutar burada tekrarlanmaz: hakedişe giren kayıt artık değiştirilemez
/// (ImmutabilityGuardInterceptor), dolayısıyla kaydın kendi tutarı zaten sabittir.
/// Toplam yine de başlıkta dondurulur; iki kaynağın ayrışmadığı testle sabit.
/// </summary>
public class ProgressPaymentRecord
{
    public int ProgressPaymentRecordId { get; set; }

    public int ProgressPaymentId { get; set; }
    public int WorkRecordId { get; set; }

    public ProgressPayment ProgressPayment { get; set; } = null!;
    public WorkRecord WorkRecord { get; set; } = null!;
}
