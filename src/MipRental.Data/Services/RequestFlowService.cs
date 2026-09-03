using Microsoft.EntityFrameworkCore;
using MipRental.Data.Approvals;
using MipRental.Domain.Approvals;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Services;

/// <summary>
/// Talep ekranlarının (Adım 11) üç controller'ında da gereken iki şeyi tek
/// yerde toplar: geçişi YAPAN kişi ve geçişin denk geldiği DÖNEM.
///
/// İş kuralı burada YOK — durum geçişlerinin tek kaynağı hâlâ
/// <see cref="RequestStateMachine"/>'dir. Bu sınıf yalnızca makinenin istediği
/// iki parametreyi veritabanından çözer.
/// </summary>
public sealed class RequestFlowService
{
    private readonly AppDbContext _db;
    private readonly ApprovalService _approvals;

    public RequestFlowService(AppDbContext db, ApprovalService approvals)
    {
        _db = db;
        _approvals = approvals;
    }

    /// <summary>
    /// Oturumdaki kullanıcının rol kodlarıyla birlikte aktör nesnesi.
    /// Çalışma kaydı tarafıyla AYNI kaynaktan okunur — iki ayrı "kullanıcının
    /// rolleri" tanımı olsaydı biri diğerinden sessizce ayrışabilirdi.
    /// </summary>
    public Task<TransitionActor> GetActorAsync(CancellationToken cancellationToken = default) =>
        _approvals.GetActorAsync(cancellationToken);

    /// <summary>
    /// Talebin dönemi: Request'te PeriodId YOKTUR, dönem işin TALEP EDİLDİĞİ
    /// tarihten türer (CLAUDE.md kural 3 ile aynı mantık — kaydın girildiği
    /// tarih değil, işin tarihi belirler).
    ///
    /// O ay için Periods satırı hiç yoksa dönem AÇIK sayılır ve kalıcı olmayan
    /// bir Period nesnesi döner: dönem satırı ancak kapatılmak için oluşturulur,
    /// yokluğu "henüz kapatılmamış" demektir. Kayıt YAZILMAZ — talep, dönemi
    /// olmayan bir aya açılabildiği için burada satır üretmek, kimsenin
    /// kapatmayacağı boş dönemler biriktirirdi.
    /// </summary>
    public async Task<Period> GetPeriodAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await _db.Periods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Year == date.Year && p.Month == date.Month, cancellationToken)
        ?? new Period { Year = date.Year, Month = date.Month, Status = PeriodStatus.OPEN };
}
