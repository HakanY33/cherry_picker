using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Web.Common;
using MipRental.Web.Models.Locations;
using MipRental.Web.Models.Shared;
using MipRental.Web.Security;

namespace MipRental.Web.Controllers;

[Authorize(Policy = PolicyNames.CanManageMaster)]
public class LocationsController : Controller
{
    private readonly AppDbContext _db;

    public LocationsController(AppDbContext db)
    {
        _db = db;
    }

    // Lokasyonlar bir ağaç yapısı olduğu için sayfa-bazlı sayfalama hiyerarşiyi
    // parçalar; bu yüzden burada sayfalama yok, tüm ağaç girintili gösterilir.
    // Arama ise eşleşen düğümü ve onu bağlama oturtan üst düğümlerini gösterir.
    public async Task<IActionResult> Index(string? search = null, bool showInactive = false)
    {
        var all = await _db.Locations.AsNoTracking()
            .Where(l => showInactive || l.IsActive)
            .ToListAsync();

        var byId = all.ToDictionary(l => l.LocationId);

        HashSet<int>? visibleIds = null;
        if (!string.IsNullOrWhiteSpace(search))
        {
            visibleIds = new HashSet<int>();
            var matches = all.Where(l =>
                l.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (l.Code is not null && l.Code.Contains(search, StringComparison.OrdinalIgnoreCase)));

            foreach (var match in matches)
            {
                Location? current = match;
                while (current is not null)
                {
                    visibleIds.Add(current.LocationId);
                    current = current.ParentLocationId is int pid && byId.TryGetValue(pid, out var parent) ? parent : null;
                }
            }
        }

        var byParent = all.ToLookup(l => l.ParentLocationId);
        var items = new List<LocationTreeItemViewModel>();

        void Visit(int? parentId, int depth)
        {
            foreach (var node in byParent[parentId].OrderBy(l => l.Name))
            {
                if (visibleIds is null || visibleIds.Contains(node.LocationId))
                {
                    items.Add(new LocationTreeItemViewModel { Location = node, Depth = depth });
                    Visit(node.LocationId, depth + 1);
                }
            }
        }

        Visit(null, 0);

        return View(new LocationIndexViewModel
        {
            Items = items,
            Page = new PageInfo { CurrentPage = 1, TotalPages = 0, Search = search, ShowInactive = showInactive }
        });
    }

    public async Task<IActionResult> Create()
    {
        var model = new LocationFormViewModel();
        await PopulateParentOptionsAsync(model, excludeId: null);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LocationFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateParentOptionsAsync(model, excludeId: null);
            return View(model);
        }

        var location = new Location
        {
            Code = model.Code,
            Name = model.Name,
            ParentLocationId = model.ParentLocationId,
            IsActive = true
        };
        location.FullPath = await BuildFullPathAsync(location);

        _db.Locations.Add(location);
        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var location = await _db.Locations.FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }

        var model = new LocationFormViewModel
        {
            LocationId = location.LocationId,
            Code = location.Code,
            Name = location.Name,
            ParentLocationId = location.ParentLocationId,
            IsActive = location.IsActive
        };
        await PopulateParentOptionsAsync(model, excludeId: id);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LocationFormViewModel model)
    {
        if (id != model.LocationId)
        {
            return NotFound();
        }

        var location = await _db.Locations.FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }

        if (model.ParentLocationId is not null)
        {
            var all = await _db.Locations.AsNoTracking().ToListAsync();
            if (TreeHelper.WouldCreateCycle(all, model.LocationId, model.ParentLocationId, l => l.LocationId, l => l.ParentLocationId))
            {
                ModelState.AddModelError(nameof(model.ParentLocationId), "Bir lokasyon kendisinin veya alt lokasyonunun altına taşınamaz.");
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateParentOptionsAsync(model, excludeId: id);
            return View(model);
        }

        var nameChanged = location.Name != model.Name;
        var parentChanged = location.ParentLocationId != model.ParentLocationId;

        location.Code = model.Code;
        location.Name = model.Name;
        location.ParentLocationId = model.ParentLocationId;
        location.IsActive = model.IsActive;

        // Kendi FullPath'ini güncelle ve isim veya parent değiştiyse tüm alt ağacı da güncelle.
        var allLocations = await _db.Locations.ToListAsync();
        var byId = allLocations.ToDictionary(l => l.LocationId);

        location.FullPath = BuildFullPath(location, byId);

        if (nameChanged || parentChanged)
        {
            RecalculateSubtreeFullPaths(location.LocationId, allLocations, byId);
        }

        await _db.SaveChangesAsync();

        TempData[TempDataKeys.SuccessMessage] = "Kayıt güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Verilen düğümün tüm alt ağacındaki her düğümün FullPath'ini yeniden hesaplar.
    /// </summary>
    private static void RecalculateSubtreeFullPaths(int rootId, List<Location> allLocations, Dictionary<int, Location> byId)
    {
        var byParent = allLocations.ToLookup(l => l.ParentLocationId);

        void Visit(int parentId)
        {
            foreach (var child in byParent[parentId])
            {
                child.FullPath = BuildFullPath(child, byId);
                Visit(child.LocationId);
            }
        }

        Visit(rootId);
    }

    private static string BuildFullPath(Location location, Dictionary<int, Location> byId)
    {
        var segments = new List<string> { location.Name };
        var currentParentId = location.ParentLocationId;
        var guard = 0;
        while (currentParentId is not null && guard++ < 50)
        {
            if (!byId.TryGetValue(currentParentId.Value, out var parent))
            {
                break;
            }

            segments.Insert(0, parent.Name);
            currentParentId = parent.ParentLocationId;
        }

        return string.Join(" > ", segments);
    }

    private async Task<string> BuildFullPathAsync(Location location)
    {
        var allLocations = await _db.Locations.AsNoTracking().ToListAsync();
        var byId = allLocations.ToDictionary(l => l.LocationId);
        return BuildFullPath(location, byId);
    }

    private async Task PopulateParentOptionsAsync(LocationFormViewModel model, int? excludeId)
    {
        var all = await _db.Locations.AsNoTracking().ToListAsync();
        var byParent = all.ToLookup(l => l.ParentLocationId);

        var excludedIds = new HashSet<int>();
        if (excludeId is int excluded)
        {
            excludedIds.Add(excluded);

            void CollectDescendants(int nodeId)
            {
                foreach (var child in byParent[nodeId])
                {
                    if (excludedIds.Add(child.LocationId))
                    {
                        CollectDescendants(child.LocationId);
                    }
                }
            }

            CollectDescendants(excluded);
        }

        var options = new List<SelectListItem>();

        void Visit(int? parentId, int depth)
        {
            foreach (var node in byParent[parentId].OrderBy(l => l.Name))
            {
                if (excludedIds.Contains(node.LocationId))
                {
                    continue;
                }

                var prefix = depth > 0 ? new string(' ', depth * 2) + "— " : string.Empty;
                options.Add(new SelectListItem(prefix + node.Name, node.LocationId.ToString(), node.LocationId == model.ParentLocationId));
                Visit(node.LocationId, depth + 1);
            }
        }

        Visit(null, 0);
        model.ParentOptions = options;
    }
}
