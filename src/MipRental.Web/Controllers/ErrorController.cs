using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MipRental.Web.Models;

namespace MipRental.Web.Controllers;

/// <summary>
/// Beklenmeyen hata sayfası. Eskiden HomeController.Error içindeydi; Home
/// kaldırıldığı için kendi controller'ına taşındı. Görünüm hâlâ
/// Views/Shared/Error.cshtml.
/// </summary>
public class ErrorController : Controller
{
    [Route("/Error")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index()
    {
        return View("Error", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
