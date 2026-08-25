using MipRental.Domain.Enums;
using MipRental.Domain.Pricing;

namespace MipRental.Tests;

// Mobilizasyon (sefer başı nakliye) bedelinin KAYIT seviyesinde bir kez uygulandığını
// veritabanısız doğrular. Satır seviyesindeki davranış PricingCalculatorTests'te.
public class RecordTotalCalculatorTests
{
    private static PricingResult LineResult(decimal lineAmount, decimal mobilizationFee = 0m, string currency = "TRY") => new()
    {
        RawQuantity = 4m,
        BillableQuantity = 4m,
        Unit = ServiceUnit.HOUR,
        UnitPriceApplied = lineAmount / 4m,
        AppliedTariff = AppliedTariff.HOURLY,
        BaseAmount = lineAmount,
        SurchargeAmount = 0m,
        MobilizationFee = mobilizationFee,
        LineAmount = lineAmount,
        Currency = currency,
        PricingRuleSnapshot = "{}",
        Explanation = Array.Empty<string>()
    };

    [Fact]
    public void Calculate_ThreeLines_MobilizationFeeAddedOnce()
    {
        var lines = new[]
        {
            LineResult(400m, mobilizationFee: 300m),
            LineResult(400m, mobilizationFee: 300m),
            LineResult(400m, mobilizationFee: 300m)
        };

        var result = RecordTotalCalculator.Calculate(lines);

        Assert.Equal(1200m, result.LinesAmount);
        Assert.Equal(300m, result.MobilizationFee); // 900 DEĞİL
        Assert.Equal(1500m, result.TotalAmount);
    }

    [Fact]
    public void Calculate_SingleLine_MobilizationFeeApplied()
    {
        var result = RecordTotalCalculator.Calculate(new[] { LineResult(400m, mobilizationFee: 300m) });

        Assert.Equal(400m, result.LinesAmount);
        Assert.Equal(300m, result.MobilizationFee);
        Assert.Equal(700m, result.TotalAmount);
    }

    [Fact]
    public void Calculate_NoMobilizationFee_TotalIsLinesOnly()
    {
        var result = RecordTotalCalculator.Calculate(new[] { LineResult(400m), LineResult(250m) });

        Assert.Equal(0m, result.MobilizationFee);
        Assert.Equal(650m, result.TotalAmount);
    }

    // Satırlar farklı sözleşme satırlarına düşüp farklı bedel taşıyabilir. Sefer bir
    // tane olduğu için bedeller TOPLANMAZ; en yüksek olanı uygulanır.
    [Fact]
    public void Calculate_DifferentFeesPerLine_HighestIsAppliedOnce()
    {
        var lines = new[]
        {
            LineResult(400m, mobilizationFee: 300m),
            LineResult(400m, mobilizationFee: 500m),
            LineResult(400m, mobilizationFee: 0m)
        };

        var result = RecordTotalCalculator.Calculate(lines);

        Assert.Equal(500m, result.MobilizationFee); // 800 değil, 300 değil
        Assert.Equal(1700m, result.TotalAmount);
    }

    [Fact]
    public void Calculate_MixedCurrencies_Throws()
    {
        var lines = new[]
        {
            LineResult(400m, mobilizationFee: 300m, currency: "TRY"),
            LineResult(400m, mobilizationFee: 300m, currency: "USD")
        };

        var ex = Assert.Throws<PricingException>(() => RecordTotalCalculator.Calculate(lines));
        Assert.Contains("para birimler", ex.Message);
    }

    [Fact]
    public void Calculate_NoLines_Throws()
    {
        Assert.Throws<PricingException>(() => RecordTotalCalculator.Calculate(Array.Empty<PricingResult>()));
    }
}
