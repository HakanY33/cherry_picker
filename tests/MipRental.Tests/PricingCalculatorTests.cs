using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Domain.Pricing;

namespace MipRental.Tests;

public class PricingCalculatorTests
{
    private static ContractLine CreateLine(
        ServiceUnit unit = ServiceUnit.HOUR,
        decimal unitPrice = 100m,
        RoundingRule roundingRule = RoundingRule.NONE,
        decimal? minBillableQuantity = null,
        decimal? dayThresholdHours = null,
        decimal? dailyPrice = null,
        decimal? mobilizationFee = null,
        decimal? maxQuantityPerRecord = null) => new()
    {
        ContractLineId = 1,
        ContractId = 1,
        ServiceId = 1,
        UnitPrice = unitPrice,
        Currency = "TRY",
        RoundingRule = roundingRule,
        MinBillableQuantity = minBillableQuantity,
        DayThresholdHours = dayThresholdHours,
        DailyPrice = dailyPrice,
        MobilizationFee = mobilizationFee,
        MaxQuantityPerRecord = maxQuantityPerRecord,
        ValidFrom = new DateOnly(2026, 1, 1),
        IsActive = true,
        ServiceCategory = new ServiceCategory { ServiceId = 1, Code = "VINC", Name = "Mobil Vinç", Unit = unit, IsActive = true }
    };

    // --- Yuvarlama ---

    [Theory]
    [InlineData(7, 10, RoundingRule.UP_30, 7.5)]
    [InlineData(7, 10, RoundingRule.UP_60, 8)]
    [InlineData(7, 10, RoundingRule.NEAREST_60, 7)]
    [InlineData(7, 40, RoundingRule.NEAREST_60, 8)]
    [InlineData(7, 30, RoundingRule.NEAREST_60, 8)] // tam ortada -> yukarı yuvarlanır
    public void Calculate_Rounding_ProducesExpectedBillableQuantity(int hours, int minutes, RoundingRule rule, decimal expected)
    {
        var line = CreateLine(roundingRule: rule);
        var request = new PricingRequest
        {
            ContractLine = line,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(8, 0).Add(TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes))
        };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(expected, result.BillableQuantity);
    }

    [Fact]
    public void Calculate_NoRounding_KeepsExactFraction()
    {
        var line = CreateLine(roundingRule: RoundingRule.NONE);
        var request = new PricingRequest
        {
            ContractLine = line,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(15, 10)
        };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(7.1667m, result.BillableQuantity);
    }

    // --- Minimum ---

    [Fact]
    public void Calculate_BelowMinimum_BillsMinimum()
    {
        var line = CreateLine(minBillableQuantity: 4m);
        var request = new PricingRequest { ContractLine = line, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(4m, result.BillableQuantity);
    }

    [Fact]
    public void Calculate_NoMinimum_BillsRawHours()
    {
        var line = CreateLine(minBillableQuantity: null);
        var request = new PricingRequest { ContractLine = line, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(2m, result.BillableQuantity);
    }

    [Fact]
    public void Calculate_RoundingAppliedBeforeMinimum()
    {
        // 1s50dk + UP_30 -> 2s (yuvarlama), sonra minimum 4 devreye girer -> 4
        var line = CreateLine(roundingRule: RoundingRule.UP_30, minBillableQuantity: 4m);
        var request = new PricingRequest
        {
            ContractLine = line,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 50)
        };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(4m, result.BillableQuantity);
    }

    // --- Gün eşiği ---

    [Fact]
    public void Calculate_AboveDayThreshold_WithDailyPrice_UsesDailyTariff()
    {
        var line = CreateLine(unitPrice: 100m, dayThresholdHours: 8m, dailyPrice: 500m);
        var request = new PricingRequest { ContractLine = line, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0) }; // 9 saat

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(AppliedTariff.DAILY, result.AppliedTariff);
        Assert.Equal(500m, result.BaseAmount);
    }

    [Fact]
    public void Calculate_AboveDayThreshold_WithoutDailyPrice_ThresholdIgnored()
    {
        var line = CreateLine(unitPrice: 100m, dayThresholdHours: 8m, dailyPrice: null);
        var request = new PricingRequest { ContractLine = line, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0) }; // 9 saat

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(AppliedTariff.HOURLY, result.AppliedTariff);
        Assert.Equal(900m, result.BaseAmount); // 9 x 100
    }

    [Fact]
    public void Calculate_ExactlyAtDayThreshold_NotExceeded_UsesHourlyTariff()
    {
        var line = CreateLine(unitPrice: 100m, dayThresholdHours: 8m, dailyPrice: 500m);
        var request = new PricingRequest { ContractLine = line, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0) }; // tam 8 saat

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(AppliedTariff.HOURLY, result.AppliedTariff);
        Assert.Equal(800m, result.BaseAmount); // 8 x 100
    }

    // --- Gece vardiyası ---

    [Fact]
    public void Calculate_SpansMidnight_ComputesCorrectDuration()
    {
        var line = CreateLine();
        var request = new PricingRequest
        {
            ContractLine = line,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(2, 0),
            SpansMidnight = true
        };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(4m, result.BillableQuantity);
    }

    [Fact]
    public void Calculate_EndBeforeStart_WithoutSpansMidnight_Throws()
    {
        var line = CreateLine();
        var request = new PricingRequest
        {
            ContractLine = line,
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(2, 0),
            SpansMidnight = false
        };

        var ex = Assert.Throws<PricingException>(() => PricingCalculator.Calculate(request));
        Assert.Contains("Gece vardiyası", ex.Message);
    }

    // --- Birim çeşitleri ---

    [Fact]
    public void Calculate_MeterUnit_NoRoundingApplied()
    {
        var line = CreateLine(unit: ServiceUnit.METER, unitPrice: 50m, roundingRule: RoundingRule.UP_60);
        var request = new PricingRequest { ContractLine = line, Quantity = 162m };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(162m, result.BillableQuantity);
        Assert.Equal(8100m, result.BaseAmount); // 162 x 50, yuvarlama yok
    }

    [Fact]
    public void Calculate_PieceUnit_MultipliesByUnitPrice()
    {
        var line = CreateLine(unit: ServiceUnit.PIECE, unitPrice: 25m);
        var request = new PricingRequest { ContractLine = line, Quantity = 12m };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(12m, result.BillableQuantity);
        Assert.Equal(300m, result.BaseAmount); // 12 x 25
    }

    // --- Ek ücret + mobilizasyon ---

    [Fact]
    public void Calculate_SurchargeMultiplier_AddsPercentageOfBaseAmount()
    {
        var line = CreateLine(unitPrice: 100m);
        var surcharge = new ContractLineSurcharge { SurchargeType = SurchargeType.NIGHT, Multiplier = 0.25m, IsActive = true };
        var request = new PricingRequest
        {
            ContractLine = line,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(10, 0), // 2 saat -> BaseAmount = 200
            ApplicableSurcharges = new[] { surcharge }
        };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(200m, result.BaseAmount);
        Assert.Equal(50m, result.SurchargeAmount); // 200 x 0.25
        Assert.Equal(250m, result.LineAmount);
    }

    [Fact]
    public void Calculate_InactiveSurcharge_IsIgnored()
    {
        var line = CreateLine(unitPrice: 100m);
        var surcharge = new ContractLineSurcharge { SurchargeType = SurchargeType.NIGHT, Multiplier = 0.25m, IsActive = false };
        var request = new PricingRequest
        {
            ContractLine = line,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(10, 0),
            ApplicableSurcharges = new[] { surcharge }
        };

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(0m, result.SurchargeAmount);
    }

    // Mobilizasyon bedeli SATIR tutarına girmez: sefer başı bir bedeldir ve kayıt
    // seviyesinde bir kez uygulanır (bkz. RecordTotalCalculatorTests).
    [Fact]
    public void Calculate_MobilizationFee_IsReportedButNotAddedToLineAmount()
    {
        var line = CreateLine(unitPrice: 100m, mobilizationFee: 300m);
        var request = new PricingRequest { ContractLine = line, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0) }; // 4 saat

        var result = PricingCalculator.Calculate(request);

        Assert.Equal(400m, result.BaseAmount); // 4 x 100
        Assert.Equal(300m, result.MobilizationFee);
        Assert.Equal(400m, result.LineAmount); // 700 DEĞİL — bedel satıra eklenmez
    }

    // --- Snapshot ---

    [Fact]
    public void Calculate_Snapshot_ContainsAppliedParameters()
    {
        var line = CreateLine(unitPrice: 1250m, roundingRule: RoundingRule.UP_30, minBillableQuantity: 4m, dayThresholdHours: 8m);
        var request = new PricingRequest { ContractLine = line, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(15, 10) }; // 7s10dk

        var result = PricingCalculator.Calculate(request);

        Assert.Contains("\"unitPrice\":1250", result.PricingRuleSnapshot);
        Assert.Contains("\"roundingRule\":\"UP_30\"", result.PricingRuleSnapshot);
        Assert.Contains("\"minBillableQuantity\":4", result.PricingRuleSnapshot);
        Assert.Contains("\"dayThresholdHours\":8", result.PricingRuleSnapshot);
        Assert.Contains("\"appliedTariff\":\"HOURLY\"", result.PricingRuleSnapshot);
        Assert.Contains("\"afterRounding\":7.5", result.PricingRuleSnapshot);
    }

    [Fact]
    public void Calculate_ResultIsFrozen_LaterContractLineMutationDoesNotAffectPreviousResult()
    {
        var line = CreateLine(unitPrice: 100m);
        var request = new PricingRequest { ContractLine = line, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) };

        var result = PricingCalculator.Calculate(request);
        var originalTotal = result.LineAmount;
        var originalSnapshot = result.PricingRuleSnapshot;

        // Sözleşme fiyatı "sonradan" değişse bile (CLAUDE.md kural 2), daha önce
        // üretilmiş PricingResult asla değişmemeli.
        line.UnitPrice = 999m;

        Assert.Equal(originalTotal, result.LineAmount);
        Assert.Equal(originalSnapshot, result.PricingRuleSnapshot);
        Assert.Equal(200m, result.LineAmount);
    }

    // --- Hata durumları ---

    [Fact]
    public void Calculate_ZeroQuantity_Throws()
    {
        var line = CreateLine(unit: ServiceUnit.PIECE);
        var request = new PricingRequest { ContractLine = line, Quantity = 0m };

        var ex = Assert.Throws<PricingException>(() => PricingCalculator.Calculate(request));
        Assert.Contains("sıfır veya negatif", ex.Message);
    }

    [Fact]
    public void Calculate_NegativeQuantity_Throws()
    {
        var line = CreateLine(unit: ServiceUnit.METER);
        var request = new PricingRequest { ContractLine = line, Quantity = -10m };

        var ex = Assert.Throws<PricingException>(() => PricingCalculator.Calculate(request));
        Assert.Contains("sıfır veya negatif", ex.Message);
    }

    [Fact]
    public void Calculate_QuantityExceedsMax_Throws()
    {
        var line = CreateLine(unit: ServiceUnit.PIECE, maxQuantityPerRecord: 10m);
        var request = new PricingRequest { ContractLine = line, Quantity = 12m };

        var ex = Assert.Throws<PricingException>(() => PricingCalculator.Calculate(request));
        Assert.Contains("azami miktarı", ex.Message);
    }

    [Fact]
    public void Calculate_HourUnit_WithoutStartOrEndTime_Throws()
    {
        var line = CreateLine();
        var request = new PricingRequest { ContractLine = line };

        var ex = Assert.Throws<PricingException>(() => PricingCalculator.Calculate(request));
        Assert.Contains("başlangıç ve bitiş saati", ex.Message);
    }

    [Fact]
    public void Calculate_NonHourUnit_WithoutQuantity_Throws()
    {
        var line = CreateLine(unit: ServiceUnit.METER);
        var request = new PricingRequest { ContractLine = line };

        var ex = Assert.Throws<PricingException>(() => PricingCalculator.Calculate(request));
        Assert.Contains("Miktar girilmedi", ex.Message);
    }
}
