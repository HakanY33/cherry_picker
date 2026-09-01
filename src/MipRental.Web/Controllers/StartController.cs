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
/// Yönlendirme sırası:
///   Firma kullanıcısı  -> kendi çalışma kayıtları (görebildiği tek veri)
///   Onaycı MIP personeli -> Onayımı Bekleyenler (günlük işi burada başlar)
///   Sözleşme yetkilisi -> Sözleşmeler
///   Diğer MIP personeli -> Çalışma Kayıtları
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
        if (_currentUser.IsFirmUser)
        {
            return RedirectToAction(nameof(WorkRecordsController.Index), "WorkRecords");
        }

        if ((await _authorization.AuthorizeAsync(User, PolicyNames.CanApprove)).Succeeded)
        {
            return RedirectToAction(nameof(ApprovalsController.Index), "Approvals");
        }

        if ((await _authorization.AuthorizeAsync(User, PolicyNames.CanManageContract)).Succeeded)
        {
            return RedirectToAction(nameof(ContractsController.Index), "Contracts");
        }

        return RedirectToAction(nameof(WorkRecordsController.Index), "WorkRecords");
    }
}
