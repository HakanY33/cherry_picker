using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.Contracts;

public class ContractIndexViewModel
{
    public IReadOnlyList<Contract> Items { get; init; } = Array.Empty<Contract>();
    public int CurrentPage { get; init; } = 1;
    public int TotalPages { get; init; }
    public int? FirmId { get; init; }
    public ContractStatus? Status { get; init; }
    public List<SelectListItem> FirmOptions { get; set; } = new();
}
