using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MipRental.Data.Services;
using MipRental.Web.Models.Verification;

namespace MipRental.Web.Controllers;

/// <summary>
/// /Dogrula/{kod} — belgenin sistemde kayıtlı olup olmadığını gösteren AÇIK sayfa.
///
/// [AllowAnonymous]: elindeki kâğıdı doğrulamak isteyen kişi (denetçi, muhasebeci,
/// firma çalışanı) sistemde hesabı olmayabilir. Güvenliği kimlik değil, tahmin
/// edilemez doğrulama kodu sağlar.
///
/// KİŞİSEL VERİ GÖSTERİLMEZ; hangi alanların döndüğü DocumentVerificationService'te
/// tek yerden sınırlanmıştır (operatör adı, telefon, onaylayan kişiler, iş tanımı,
/// lokasyon ve dosya yolu hiç çekilmez).
/// </summary>
[AllowAnonymous]
[Route("Dogrula")]
public class VerificationController : Controller
{
    private readonly DocumentVerificationService _verification;

    public VerificationController(DocumentVerificationService verification)
    {
        _verification = verification;
    }

    [HttpGet("{code?}")]
    public async Task<IActionResult> Index(string? code)
    {
        var result = await _verification.VerifyAsync(code);
        return View(new VerificationViewModel { Code = code, Result = result });
    }
}
