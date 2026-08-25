using MipRental.Domain.Entities;
using MipRental.Web.Models.Shared;

namespace MipRental.Web.Models.Locations;

public class LocationTreeItemViewModel
{
    public Location Location { get; init; } = null!;
    public int Depth { get; init; }
}

public class LocationIndexViewModel
{
    public IReadOnlyList<LocationTreeItemViewModel> Items { get; init; } = Array.Empty<LocationTreeItemViewModel>();

    // Sayfalama yapılmıyor (bkz. controller açıklaması); _SearchBox partial'ını
    // değişiklik yapmadan yeniden kullanmak için TotalPages=0 ile dolduruluyor.
    public PageInfo Page { get; init; } = new();
}
