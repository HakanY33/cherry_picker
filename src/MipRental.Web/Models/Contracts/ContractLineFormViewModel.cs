using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using MipRental.Domain.Enums;

namespace MipRental.Web.Models.Contracts;

public class ContractLineFormViewModel
{
    public int ContractLineId { get; set; }
    public int ContractId { get; set; }

    [Required(ErrorMessage = "Hizmet seçimi zorunludur.")]
    [Display(Name = "Hizmet")]
    public int ServiceId { get; set; }

    [Display(Name = "Varyant")]
    public int? VariantId { get; set; }

    [Required(ErrorMessage = "Birim fiyat zorunludur.")]
    [Range(0.0001, double.MaxValue, ErrorMessage = "Birim fiyat sıfırdan büyük olmalıdır.")]
    [Display(Name = "Birim Fiyat")]
    public decimal UnitPrice { get; set; }

    [Required(ErrorMessage = "Para birimi zorunludur.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Para birimi 3 karakter olmalıdır (ör. TRY).")]
    [Display(Name = "Para Birimi")]
    public string Currency { get; set; } = "TRY";

    [Display(Name = "Minimum Faturalanabilir Miktar")]
    public decimal? MinBillableQuantity { get; set; }

    [Display(Name = "Yuvarlama Kuralı")]
    public RoundingRule RoundingRule { get; set; } = RoundingRule.NONE;

    [Display(Name = "Gün Eşiği (Saat)")]
    public decimal? DayThresholdHours { get; set; }

    [Display(Name = "Günlük Fiyat")]
    public decimal? DailyPrice { get; set; }

    [Display(Name = "Sabit Bedel (Mobilizasyon)")]
    public decimal? MobilizationFee { get; set; }

    [Display(Name = "Kayıt Başına Maksimum Miktar")]
    public decimal? MaxQuantityPerRecord { get; set; }

    [Required(ErrorMessage = "Geçerlilik başlangıç tarihi zorunludur.")]
    [Display(Name = "Geçerlilik Başlangıcı")]
    public DateOnly ValidFrom { get; set; }

    [Display(Name = "Geçerlilik Bitişi")]
    public DateOnly? ValidTo { get; set; }

    public List<SelectListItem> ServiceOptions { get; set; } = new();
    public List<SelectListItem> VariantOptions { get; set; } = new();
    public List<SelectListItem> RoundingRuleOptions { get; set; } = new();
}
