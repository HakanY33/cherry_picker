using MipRental.Domain.Entities;

namespace MipRental.Web.Models.Contracts;

public class PriceOnDateViewModel
{
    public int ContractId { get; set; }
    public DateOnly Date { get; set; }
    public List<ContractLine> Lines { get; set; } = new();
}
