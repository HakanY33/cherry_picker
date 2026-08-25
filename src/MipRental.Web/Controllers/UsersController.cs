using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Common;
using MipRental.Web.Models.Users;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanManageUsers)]
public class UsersController : Controller
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordHasher<User> _passwordHasher;

    public UsersController(AppDbContext db, ICurrentUser currentUser, IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _currentUser = currentUser;
        _passwordHasher = passwordHasher;
    }

    public async Task<IActionResult> Index(int page = 1, string? search = null, bool showInactive = false)
    {
        var query = _db.Users.Include(u => u.Firm).Include(u => u.UserRoles).ThenInclude(ur => ur.Role).AsQueryable();

        // Firma adminleri sadece kendi firmalarının kullanıcılarını görebilir.
        // Users tablosunda global query filter yok (bkz. AppDbContext); bu kısıtlama
        // burada, controller seviyesinde, sunucu tarafında bilinçli olarak uygulanır.
        if (_currentUser.IsFirmUser)
        {
            query = query.Where(u => u.FirmId == _currentUser.FirmId);
        }

        if (!showInactive)
        {
            query = query.Where(u => u.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                u.UserName.Contains(search) || u.FullName.Contains(search) || (u.Email != null && u.Email.Contains(search)));
        }

        var model = await query.OrderBy(u => u.FullName).ToPagedListAsync(page, search, showInactive);
        return View(model);
    }

    public async Task<IActionResult> Create()
    {
        var model = new UserFormViewModel { IsEdit = false };

        if (_currentUser.IsFirmUser)
        {
            model.FirmId = _currentUser.FirmId;
            model.CanChooseFirm = false;
        }
        else
        {
            model.CanChooseFirm = true;
        }

        await PopulateOptionsAsync(model, currentFirmId: null);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormViewModel model)
    {
        model.IsEdit = false;

        // Firma admini SADECE kendi firmasına kullanıcı oluşturabilir. Formdan farklı
        // bir FirmId gelse bile (tampered POST) kendi firmasına sabitlenir.
        if (_currentUser.IsFirmUser)
        {
            model.FirmId = _currentUser.FirmId;
            model.DepartmentId = null;
            model.CanChooseFirm = false;
        }
        else
        {
            model.CanChooseFirm = true;
        }

        await ValidateAsync(model, isCreate: true);
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, currentFirmId: null);
            return View(model);
        }

        var user = new User
        {
            UserName = model.UserName,
            FullName = model.FullName,
            Email = model.Email,
            Phone = model.Phone,
            Position = model.Position,
            FirmId = model.FirmId,
            DepartmentId = model.DepartmentId,
            IsFirmAdmin = model.FirmId is not null && model.IsFirmAdmin,
            IsActive = true
        };

        var generatedPassword = PasswordGenerator.Generate();
        user.PasswordHash = _passwordHasher.HashPassword(user, generatedPassword);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        foreach (var roleId in model.SelectedRoleIds.Distinct())
        {
            _db.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = roleId });
        }
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt oluşturuldu.";
        TempData["GeneratedPassword"] = generatedPassword;
        TempData["GeneratedPasswordUserName"] = user.UserName;
        return RedirectToAction(nameof(PasswordDisplay));
    }

    // Şifre yalnızca bu yönlendirmede, TempData üzerinden bir kez gösterilir;
    // TempData indexer okunduğunda otomatik olarak işaretlenip bir sonraki
    // istekte temizlenir, sayfa yenilense bile şifre tekrar görünmez.
    public IActionResult PasswordDisplay()
    {
        if (TempData["GeneratedPassword"] is not string password || TempData["GeneratedPasswordUserName"] is not string userName)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new UserCreatedViewModel { UserName = userName, GeneratedPassword = password });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var user = await _db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.UserId == id);
        if (user is null)
        {
            return NotFound();
        }

        // Firma admini başka firmanın kullanıcısını Id ile bile göremez/düzenleyemez.
        if (_currentUser.IsFirmUser && user.FirmId != _currentUser.FirmId)
        {
            return NotFound();
        }

        var model = new UserFormViewModel
        {
            UserId = user.UserId,
            UserName = user.UserName,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            Position = user.Position,
            FirmId = user.FirmId,
            DepartmentId = user.DepartmentId,
            IsFirmAdmin = user.IsFirmAdmin,
            IsActive = user.IsActive,
            SelectedRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToList(),
            IsEdit = true,
            CanChooseFirm = false // FirmId hiçbir zaman düzenleme ekranından değiştirilemez
        };
        await PopulateOptionsAsync(model, currentFirmId: user.FirmId);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UserFormViewModel model)
    {
        if (id != model.UserId)
        {
            return NotFound();
        }

        var user = await _db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.UserId == id);
        if (user is null)
        {
            return NotFound();
        }

        if (_currentUser.IsFirmUser && user.FirmId != _currentUser.FirmId)
        {
            return NotFound();
        }

        model.IsEdit = true;
        model.CanChooseFirm = false;

        // Kullanıcı adı ve firma DEĞİŞTİRİLEMEZ; düzenleme formu bu alanları
        // GÖNDERMEZ, değerleri veritabanından okunur. Ama model bağlama forma
        // bakarak doğrulamayı çoktan yapmış ve UserName için [Required] hatasını
        // ModelState'e yazmıştır. Aşağıdaki atama o hatayı temizlemez — bu yüzden
        // ModelState kayıtları da düşürülür; yoksa ekran hiçbir zaman kaydedilemez.
        ModelState.Remove(nameof(model.UserName));
        ModelState.Remove(nameof(model.FirmId));

        model.UserName = user.UserName;
        model.FirmId = user.FirmId;
        if (_currentUser.IsFirmUser)
        {
            model.DepartmentId = null;
        }

        await ValidateAsync(model, isCreate: false);
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, currentFirmId: user.FirmId);
            return View(model);
        }

        user.FullName = model.FullName;
        user.Email = model.Email;
        user.Phone = model.Phone;
        user.Position = model.Position;
        user.DepartmentId = model.DepartmentId;
        user.IsFirmAdmin = user.FirmId is not null && model.IsFirmAdmin;
        user.IsActive = model.IsActive;

        var selectedRoleIds = model.SelectedRoleIds.Distinct().ToHashSet();
        var currentRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToHashSet();

        foreach (var toRemove in user.UserRoles.Where(ur => !selectedRoleIds.Contains(ur.RoleId)).ToList())
        {
            _db.UserRoles.Remove(toRemove);
        }

        foreach (var roleId in selectedRoleIds.Except(currentRoleIds))
        {
            _db.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = roleId });
        }

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // Firma seçimi (yalnızca MIP admini için) değiştiğinde rol listesini, seçilen
    // firmanın kapsamına (INTERNAL/EXTERNAL) göre htmx ile anlık tazeler.
    [HttpGet]
    public async Task<IActionResult> RoleOptions(int? firmId)
    {
        var scope = firmId is null ? RoleScope.INTERNAL : RoleScope.EXTERNAL;
        var roles = await _db.Roles.Where(r => r.Scope == scope).OrderBy(r => r.Name).ToListAsync();
        var items = roles.Select(r => new RoleCheckboxItem { RoleId = r.RoleId, Name = r.Name, Selected = false }).ToList();
        return PartialView("_RoleCheckboxList", items);
    }

    private async Task ValidateAsync(UserFormViewModel model, bool isCreate)
    {
        if (isCreate)
        {
            var userNameTaken = await _db.Users.AnyAsync(u => u.UserName == model.UserName);
            if (userNameTaken)
            {
                ModelState.AddModelError(nameof(model.UserName), "Bu kullanıcı adı zaten kullanılıyor.");
            }
        }

        if (model.FirmId is null && model.IsFirmAdmin)
        {
            // MIP personeli firma admini olamaz; UI zaten göstermez, savunma amaçlı.
            model.IsFirmAdmin = false;
        }

        var allowedScope = model.FirmId is null ? RoleScope.INTERNAL : RoleScope.EXTERNAL;
        if (model.SelectedRoleIds.Count > 0)
        {
            var invalidScopeSelected = await _db.Roles
                .Where(r => model.SelectedRoleIds.Contains(r.RoleId) && r.Scope != allowedScope)
                .AnyAsync();
            if (invalidScopeSelected)
            {
                ModelState.AddModelError(nameof(model.SelectedRoleIds), "Seçilen rollerden biri bu kullanıcı tipi için geçerli değil.");
            }
        }
    }

    private async Task PopulateOptionsAsync(UserFormViewModel model, int? currentFirmId)
    {
        if (_currentUser.IsMipStaff)
        {
            var firms = await _db.Firms.Where(f => f.IsActive || f.FirmId == currentFirmId).OrderBy(f => f.Title).ToListAsync();
            model.FirmOptions = firms
                .Select(f => new SelectListItem(f.IsActive ? f.Title : $"{f.Title} (Pasif)", f.FirmId.ToString(), f.FirmId == model.FirmId))
                .ToList();
        }

        var departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync();
        model.DepartmentOptions = departments
            .Select(d => new SelectListItem(d.Name, d.DepartmentId.ToString(), d.DepartmentId == model.DepartmentId))
            .ToList();

        var scope = model.FirmId is null ? RoleScope.INTERNAL : RoleScope.EXTERNAL;
        var roles = await _db.Roles.Where(r => r.Scope == scope).OrderBy(r => r.Name).ToListAsync();
        model.RoleOptions = roles
            .Select(r => new RoleCheckboxItem { RoleId = r.RoleId, Name = r.Name, Selected = model.SelectedRoleIds.Contains(r.RoleId) })
            .ToList();
    }
}
