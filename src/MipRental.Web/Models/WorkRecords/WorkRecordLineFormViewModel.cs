using Microsoft.AspNetCore.Mvc.Rendering;

namespace MipRental.Web.Models.WorkRecords;

public class WorkRecordLineFormViewModel
{
    // Model binding'de Lines[Index].ServiceId gibi indeksli isimler için kullanılır.
    public int Index { get; set; }

    public int WorkRecordLineId { get; set; }

    public int ServiceId { get; set; }
    public int? VariantId { get; set; }

    // HOUR dışındaki birimler (METER, PIECE, DAY, SHIFT) için doğrudan miktar.
    // HOUR biriminde kaydın başlangıç/bitiş saatinden hesaplanır, bu alan yok sayılır.
    public decimal? Quantity { get; set; }

    public List<SelectListItem> ServiceOptions { get; set; } = new();
    public List<SelectListItem> VariantOptions { get; set; } = new();
}
