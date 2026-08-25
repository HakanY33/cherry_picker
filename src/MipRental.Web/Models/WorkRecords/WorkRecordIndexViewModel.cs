using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.WorkRecords;

public class WorkRecordIndexViewModel
{
    public IReadOnlyList<WorkRecord> Items { get; init; } = Array.Empty<WorkRecord>();
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }

    // MIP personeli için: tüm firmalar görünür ve firma filtresi sunulur.
    // Firma kullanıcısı için query filter zaten sadece kendi firmasını döner; filtre gizlenir.
    public bool ShowFirmFilter { get; init; }
    public int? FirmId { get; init; }
    public int? PeriodId { get; init; }
    public WorkRecordStatus? Status { get; init; }

    public List<SelectListItem> FirmOptions { get; init; } = new();
    public List<SelectListItem> PeriodOptions { get; init; } = new();
}
