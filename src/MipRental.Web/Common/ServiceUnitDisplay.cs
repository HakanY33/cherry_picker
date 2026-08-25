using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

public static class ServiceUnitDisplay
{
    public static readonly IReadOnlyDictionary<ServiceUnit, string> Labels = new Dictionary<ServiceUnit, string>
    {
        [ServiceUnit.HOUR] = "Saat",
        [ServiceUnit.DAY] = "Gün",
        [ServiceUnit.SHIFT] = "Vardiya",
        [ServiceUnit.METER] = "Metre",
        [ServiceUnit.PIECE] = "Adet"
    };

    public static string GetLabel(ServiceUnit unit) => Labels.TryGetValue(unit, out var label) ? label : unit.ToString();

    public static List<SelectListItem> ToSelectList(ServiceUnit? selected = null) =>
        Labels.Select(kv => new SelectListItem(kv.Value, kv.Key.ToString(), kv.Key == selected)).ToList();
}
