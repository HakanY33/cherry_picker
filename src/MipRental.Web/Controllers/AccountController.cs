using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Web.Models;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly LoginValidator _loginValidator;

    public AccountController(AppDbContext db, LoginValidator loginValidator)
    {
        _db = db;
        _loginValidator = loginValidator;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _loginValidator.ValidateAsync(model.UserName, model.Password);
        if (user is null)
        {
            // Kullanıcı adı mı şifre mi hatalı belli edilmez; tek genel mesaj.
            ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(AppClaimTypes.IsFirmAdmin, user.IsFirmAdmin ? "true" : "false"),
        };

        if (user.FirmId is not null)
        {
            claims.Add(new Claim(AppClaimTypes.FirmId, user.FirmId.Value.ToString()));
        }

        if (user.DepartmentId is not null)
        {
            claims.Add(new Claim(AppClaimTypes.DepartmentId, user.DepartmentId.Value.ToString()));
        }

        var roleCodes = await _db.UserRoles
            .Where(ur => ur.UserId == user.UserId)
            .Select(ur => ur.Role.Code)
            .ToListAsync();
        claims.AddRange(roleCodes.Select(code => new Claim(ClaimTypes.Role, code)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (returnUrl is not null && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
