using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Domain.Entities;
using MipRental.Web.Controllers;
using MipRental.Web.Models.Locations;

namespace MipRental.Tests;

public class LocationFullPathTests
{
    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, new FakeCurrentUser());
    }

    private static LocationsController CreateController(AppDbContext db)
    {
        var controller = new LocationsController(db)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
        };
        return controller;
    }

    [Fact]
    public async Task RenamingTopNode_UpdatesFullPathOfAllDescendants()
    {
        var dbName = Guid.NewGuid().ToString();

        // 3 seviyeli ağaç kur: Liman > Rıhtım > İskele
        await using (var db = CreateContext(dbName))
        {
            db.Locations.AddRange(
                new Location { LocationId = 1, Name = "Liman", FullPath = "Liman", IsActive = true },
                new Location { LocationId = 2, Name = "Rıhtım", ParentLocationId = 1, FullPath = "Liman > Rıhtım", IsActive = true },
                new Location { LocationId = 3, Name = "İskele", ParentLocationId = 2, FullPath = "Liman > Rıhtım > İskele", IsActive = true });
            await db.SaveChangesAsync();
        }

        // En üstteki düğümün adını değiştir
        await using (var db = CreateContext(dbName))
        {
            var controller = CreateController(db);
            var model = new LocationFormViewModel
            {
                LocationId = 1,
                Name = "Yeni Liman",
                ParentLocationId = null,
                IsActive = true
            };

            await controller.Edit(1, model);
        }

        // Tüm düğümlerin FullPath'lerinin güncellendiğini doğrula
        await using (var db = CreateContext(dbName))
        {
            var top = await db.Locations.SingleAsync(l => l.LocationId == 1);
            var mid = await db.Locations.SingleAsync(l => l.LocationId == 2);
            var leaf = await db.Locations.SingleAsync(l => l.LocationId == 3);

            Assert.Equal("Yeni Liman", top.FullPath);
            Assert.Equal("Yeni Liman > Rıhtım", mid.FullPath);
            Assert.Equal("Yeni Liman > Rıhtım > İskele", leaf.FullPath);
        }
    }

    [Fact]
    public async Task ChangingParent_UpdatesFullPathOfMovedSubtree()
    {
        var dbName = Guid.NewGuid().ToString();

        // Ağaç: A > B > C  ve ayrı bir D düğümü
        await using (var db = CreateContext(dbName))
        {
            db.Locations.AddRange(
                new Location { LocationId = 1, Name = "A", FullPath = "A", IsActive = true },
                new Location { LocationId = 2, Name = "B", ParentLocationId = 1, FullPath = "A > B", IsActive = true },
                new Location { LocationId = 3, Name = "C", ParentLocationId = 2, FullPath = "A > B > C", IsActive = true },
                new Location { LocationId = 4, Name = "D", FullPath = "D", IsActive = true });
            await db.SaveChangesAsync();
        }

        // B'yi D'nin altına taşı: D > B > C
        await using (var db = CreateContext(dbName))
        {
            var controller = CreateController(db);
            var model = new LocationFormViewModel
            {
                LocationId = 2,
                Name = "B",
                ParentLocationId = 4,
                IsActive = true
            };

            await controller.Edit(2, model);
        }

        await using (var db = CreateContext(dbName))
        {
            var b = await db.Locations.SingleAsync(l => l.LocationId == 2);
            var c = await db.Locations.SingleAsync(l => l.LocationId == 3);

            Assert.Equal("D > B", b.FullPath);
            Assert.Equal("D > B > C", c.FullPath);
        }
    }

    private sealed class NoOpTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
