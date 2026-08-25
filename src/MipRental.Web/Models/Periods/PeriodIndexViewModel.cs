using MipRental.Domain.Entities;

namespace MipRental.Web.Models.Periods;

public class PeriodIndexViewModel
{
    public IReadOnlyList<Period> Items { get; init; } = Array.Empty<Period>();
    public int? Year { get; init; }
    public List<int> YearOptions { get; init; } = new();
}
