using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Reporting;

namespace MipRental.Web.Models.Summaries;

/// <summary>Aylık icmal ekranının modeli: filtre kutuları + hesaplanmış icmal.</summary>
public class MonthlySummaryViewModel
{
    public int? PeriodId { get; set; }
    public int? FirmId { get; set; }
    public int? ServiceId { get; set; }

    public List<SelectListItem> PeriodOptions { get; set; } = new();
    public List<SelectListItem> FirmOptions { get; set; } = new();
    public List<SelectListItem> ServiceOptions { get; set; } = new();

    /// <summary>Zorunlu filtreler seçilene kadar null kalır.</summary>
    public MonthlySummary? Summary { get; set; }

    /// <summary>
    /// Firma kullanıcısının firması sabittir; firma seçim kutusu gösterilmez
    /// (CLAUDE.md kural 7 — seçenek sunmak bile yanıltıcı olur).
    /// </summary>
    public bool CanChooseFirm { get; set; }
}
