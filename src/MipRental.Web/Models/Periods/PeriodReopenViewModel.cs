using System.ComponentModel.DataAnnotations;

namespace MipRental.Web.Models.Periods;

public class PeriodReopenViewModel
{
    public int PeriodId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    [StringLength(500, ErrorMessage = "Gerekçe en fazla 500 karakter olabilir.")]
    [Display(Name = "Yeniden Açma Gerekçesi")]
    public string? ReopenReason { get; set; }
}
