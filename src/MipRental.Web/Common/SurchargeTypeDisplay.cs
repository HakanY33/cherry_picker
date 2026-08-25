using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

public static class SurchargeTypeDisplay
{
    public static readonly IReadOnlyDictionary<SurchargeType, string> Labels = new Dictionary<SurchargeType, string>
    {
        [SurchargeType.OVERTIME] = "Mesai",
        [SurchargeType.NIGHT] = "Gece",
        [SurchargeType.WEEKEND] = "Hafta Sonu",
        [SurchargeType.HOLIDAY] = "Resmi Tatil"
    };

    public static string GetLabel(SurchargeType type) => Labels.TryGetValue(type, out var label) ? label : type.ToString();

    public static List<SelectListItem> ToSelectList(SurchargeType? selected = null) =>
        Labels.Select(kv => new SelectListItem(kv.Value, kv.Key.ToString(), kv.Key == selected)).ToList();
}
