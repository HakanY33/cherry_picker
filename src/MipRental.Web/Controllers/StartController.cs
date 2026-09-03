using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MipRental.Domain.Abstractions;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

/// <summary>
/// Uygulamanın giriş noktası. Varsayılan şablondan gelen Home/Index karşılama
/// sayfasının yerini alır — kendi içeriği YOKTUR, tek işi kullanıcıyı rolüne
/// uygun başlangıç ekranına yönlendirmektir.
///
/// Sıralamanın kuralı: HERKESİ GÜNLÜK İŞİNİN BAŞLADIĞI YERE bırak. Talep akışı
/// (Adım 11) sistemin ana giriş kapısı olduğu için talep rolleri en üstte;
/// çalışma kaydı tarafı onların altında kalır.
///
/// Yönlendirme sırası:
///   Talep açan            -> Taleplerim
///   Ekipman Müdürlüğü     -> Onay bekleyen talepler
///   Firma yetkilisi       -> Bekleyen talepler   (çalışma kayıtlarından ÖNCE:
///                            firma yetkilisi de bir firma kullanıcısıdır, genel
///                            firma yönlendirmesi onu yanlış ekrana götürürdü)
///   Diğer firma kullanıcısı -> kendi çalışma kayıtları
///   Onaycı MIP personeli  -> Onayımı Bekleyenler
///   Sözleşme yetkilisi    -> Sözleşmeler
///   Diğer MIP personeli   -> Çalışma Kayıtları
/// </summary>
[Authorize]
public class StartController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IAuthorizationService _authorization;

    public StartController(ICurrentUser currentUser, IAuthorizationService authorization)
    {
        _currentUser = currentUser;
        _authorization = authorization;
    }

    public async Task<IActionResult> Index()
    {
        if (await AllowedAsync(PolicyNames.CanCreateRequest))
        {
            return RedirectToAction(nameof(RequestsController.Index), "Requests");
        }

        if (await AllowedAsync(PolicyNames.CanViewEquipmentRequests))
        {
            return RedirectToAction(nameof(EquipmentRequestsController.Index), "EquipmentRequests");
        }

        if (await AllowedAsync(PolicyNames.CanManageFirmRequests))
        {
            return RedirectToAction(nameof(FirmRequestsController.Index), "FirmRequests");
        }

        if (_currentUser.IsFirmUser)
        {
            return RedirectToAction(nameof(WorkRecordsController.Index), "WorkRecords");
        }

        if (await AllowedAsync(PolicyNames.CanApprove))
        {
            return RedirectToAction(nameof(ApprovalsController.Index), "Approvals");
        }

        if (await AllowedAsync(PolicyNames.CanManageContract))
        {
            return RedirectToAction(nameof(ContractsController.Index), "Contracts");
        }

        return RedirectToAction(nameof(WorkRecordsController.Index), "WorkRecords");
    }

    private async Task<bool> AllowedAsync(string policy) =>
        (await _authorization.AuthorizeAsync(User, policy)).Succeeded;
}
