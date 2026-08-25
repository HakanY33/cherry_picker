using MipRental.Domain.Entities;

namespace MipRental.Web.Models.Contracts;

public class ContractDetailsViewModel
{
    public Contract Contract { get; set; } = null!;
    public List<ContractLine> Lines { get; set; } = new();

    // Bağlı WorkRecordLine'ı olan satırların Id'leri; "Düzelt" butonunun
    // gösterilip gösterilmeyeceğine görünüm bunlara bakarak karar verir.
    public HashSet<int> LinesWithWorkRecords { get; set; } = new();

    public PriceOnDateViewModel PriceOnDatePreview { get; set; } = new();
}
