using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Common;

public static class RoundingRuleDisplay
{
    public static readonly IReadOnlyDictionary<RoundingRule, string> Labels = new Dictionary<RoundingRule, string>
    {
        [RoundingRule.NONE] = "Yuvarlama Yok",
        [RoundingRule.UP_15] = "15 Dakikaya Yukarı Yuvarla",
        [RoundingRule.UP_30] = "30 Dakikaya Yukarı Yuvarla",
        [RoundingRule.UP_60] = "60 Dakikaya Yukarı Yuvarla",
        [RoundingRule.NEAREST_15] = "En Yakın 15 Dakikaya Yuvarla",
        [RoundingRule.NEAREST_30] = "En Yakın 30 Dakikaya Yuvarla",
        [RoundingRule.NEAREST_60] = "En Yakın 60 Dakikaya Yuvarla"
    };

    public static string GetLabel(RoundingRule rule) => Labels.TryGetValue(rule, out var label) ? label : rule.ToString();

    public static List<SelectListItem> ToSelectList(RoundingRule? selected = null) =>
        Labels.Select(kv => new SelectListItem(kv.Value, kv.Key.ToString(), kv.Key == selected)).ToList();
}
