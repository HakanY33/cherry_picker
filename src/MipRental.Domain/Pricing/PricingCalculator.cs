using System.Globalization;
using System.Text.Json;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Domain.Pricing;

// Saf hesaplama çekirdeği: girdi olarak PricingRequest (ContractLine + miktar/saat) alır,
// PricingResult döner. Veritabanı erişimi, DateTime.Now/UtcNow veya başka bir dış
// bağımlılık YOK — bu sayede veritabanısız test edilebilir.
//
// Doğru ContractLine'ı sözleşmeden bulmak ContractLineResolver'ın işi (MipRental.Data
// projesinde, çünkü DbContext'e ihtiyaç duyar). İkisini birleştirmeyin.
//
// Ek ücretlerin (surcharge) hangi çalışma kaydına UYGULANACAĞINA bu sınıf karar vermez
// — gece/hafta sonu/tatil tespiti bir takvim kuralı gerektirir ve bu adımın kapsamı
// dışındadır (bkz. rapor notu). PricingRequest.ApplicableSurcharges zaten uygulanacağı
// belirlenmiş listedir; bu sınıf sadece tutarını (çarpan × BaseAmount + sabit tutar)
// toplar.
//
// KAPSAM: bu sınıf SATIR tutarını hesaplar. Mobilizasyon (sefer başı nakliye) bedeli
// satır değil KAYIT seviyesindedir — çok satırlı bir kayıtta aynı sefer için birden
// fazla kez faturalanmaması gerekir. Bu yüzden mobilizasyon bedeli LineAmount'a
// EKLENMEZ; sadece bilgi olarak PricingResult.MobilizationFee'de taşınır ve kayıt
// toplamına RecordTotalCalculator tarafından bir kez eklenir.
public static class PricingCalculator
{
    private static readonly CultureInfo TrCulture = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly IReadOnlyDictionary<ServiceUnit, string> UnitLabels = new Dictionary<ServiceUnit, string>
    {
        [ServiceUnit.HOUR] = "saat",
        [ServiceUnit.DAY] = "gün",
        [ServiceUnit.SHIFT] = "vardiya",
        [ServiceUnit.METER] = "metre",
        [ServiceUnit.PIECE] = "adet"
    };

    public static PricingResult Calculate(PricingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var line = request.ContractLine;
        var unit = line.ServiceCategory.Unit;
        var explanation = new List<string>();

        var rawQuantity = ComputeRawQuantity(request, unit, explanation);
        if (rawQuantity <= 0m)
        {
            throw new PricingException("Miktar sıfır veya negatif olamaz.");
        }

        var roundedQuantity = unit == ServiceUnit.HOUR
            ? ApplyRounding(rawQuantity, line.RoundingRule, explanation)
            : rawQuantity;

        var billableQuantity = ApplyMinimum(roundedQuantity, line.MinBillableQuantity, unit, explanation);

        if (line.MaxQuantityPerRecord is decimal max && billableQuantity > max)
        {
            throw new PricingException(
                $"Girilen miktar ({FormatQty(billableQuantity)} {UnitLabel(unit)}), bu fiyat satırı için izin verilen azami miktarı ({FormatQty(max)} {UnitLabel(unit)}) aşıyor.");
        }

        var (appliedTariff, unitPriceApplied, baseAmount) = ApplyDayThreshold(line, billableQuantity, explanation);

        explanation.Add(appliedTariff == AppliedTariff.DAILY
            ? $"{FormatMoney(unitPriceApplied)} {line.Currency} (günlük tarife) = {FormatMoney(baseAmount)} {line.Currency}"
            : $"{FormatQty(billableQuantity)} × {FormatMoney(unitPriceApplied)} = {FormatMoney(baseAmount)} {line.Currency}");

        var surchargeAmount = ApplySurcharges(request.ApplicableSurcharges, baseAmount, line.Currency, explanation);

        var lineAmount = baseAmount + surchargeAmount;
        explanation.Add($"Satır tutarı: {FormatMoney(lineAmount)} {line.Currency}");

        // Sefer başı bedel satıra eklenmez; kaydın tamamına bir kez uygulanır.
        var mobilizationFee = line.MobilizationFee ?? 0m;
        if (mobilizationFee > 0m)
        {
            explanation.Add(
                $"Mobilizasyon bedeli: {FormatMoney(mobilizationFee)} {line.Currency} (sefer başına, kayıt toplamına bir kez eklenir — satır tutarına dahil değildir)");
        }

        // Açıklama satırları da snapshot'a yazılır: itirazda kanıt olan metin,
        // tutarın kendisi kadar dondurulmalı (kural 2). Onay ekranı fiyat
        // açıklamasını buradan okur, yeniden hesaplamaz.
        var snapshot = BuildSnapshot(line, request, rawQuantity, roundedQuantity, billableQuantity,
            appliedTariff, unitPriceApplied, baseAmount, surchargeAmount, lineAmount, explanation);

        return new PricingResult
        {
            RawQuantity = rawQuantity,
            BillableQuantity = billableQuantity,
            Unit = unit,
            UnitPriceApplied = unitPriceApplied,
            AppliedTariff = appliedTariff,
            BaseAmount = baseAmount,
            SurchargeAmount = surchargeAmount,
            MobilizationFee = mobilizationFee,
            LineAmount = lineAmount,
            Currency = line.Currency,
            PricingRuleSnapshot = snapshot,
            Explanation = explanation
        };
    }

    private static decimal ComputeRawQuantity(PricingRequest request, ServiceUnit unit, List<string> explanation)
    {
        if (unit == ServiceUnit.HOUR)
        {
            if (request.StartTime is null || request.EndTime is null)
            {
                throw new PricingException("Saatlik hizmetler için başlangıç ve bitiş saati girilmelidir.");
            }

            var start = request.StartTime.Value;
            var end = request.EndTime.Value;

            TimeSpan duration;
            if (end < start)
            {
                if (!request.SpansMidnight)
                {
                    throw new PricingException(
                        "Bitiş saati başlangıç saatinden önce olamaz. Gece vardiyasıysa gece vardiyası işaretlenmelidir.");
                }

                duration = (TimeSpan.FromHours(24) - start.ToTimeSpan()) + end.ToTimeSpan();
            }
            else
            {
                duration = end.ToTimeSpan() - start.ToTimeSpan();
            }

            explanation.Add($"Ham süre: {duration.Hours} saat {duration.Minutes} dakika");

            return Math.Round(duration.Ticks / (decimal)TimeSpan.TicksPerHour, 4, MidpointRounding.AwayFromZero);
        }

        if (request.Quantity is null)
        {
            throw new PricingException("Miktar girilmedi.");
        }

        explanation.Add($"Ham miktar: {FormatQty(request.Quantity.Value)} {UnitLabel(unit)}");
        return request.Quantity.Value;
    }

    private static decimal ApplyRounding(decimal hours, RoundingRule rule, List<string> explanation)
    {
        if (rule == RoundingRule.NONE)
        {
            return hours;
        }

        var incrementMinutes = rule switch
        {
            RoundingRule.UP_15 or RoundingRule.NEAREST_15 => 15,
            RoundingRule.UP_30 or RoundingRule.NEAREST_30 => 30,
            RoundingRule.UP_60 or RoundingRule.NEAREST_60 => 60,
            _ => throw new PricingException($"Bilinmeyen yuvarlama kuralı: {rule}.")
        };
        var isRoundUp = rule is RoundingRule.UP_15 or RoundingRule.UP_30 or RoundingRule.UP_60;

        var increment = incrementMinutes / 60m;
        var units = hours / increment;
        var roundedUnits = isRoundUp ? Math.Ceiling(units) : Math.Floor(units + 0.5m);
        var rounded = roundedUnits * increment;

        explanation.Add(isRoundUp
            ? $"{incrementMinutes} dakikaya yukarı yuvarlandı: {FormatQty(rounded)} saat"
            : $"En yakın {incrementMinutes} dakikaya yuvarlandı: {FormatQty(rounded)} saat");

        return rounded;
    }

    private static decimal ApplyMinimum(decimal quantity, decimal? minimum, ServiceUnit unit, List<string> explanation)
    {
        if (minimum is null)
        {
            return quantity;
        }

        if (quantity < minimum.Value)
        {
            explanation.Add(
                $"Minimum {FormatQty(minimum.Value)} {UnitLabel(unit)} altında kaldı, {FormatQty(minimum.Value)} {UnitLabel(unit)} olarak faturalandı.");
            return minimum.Value;
        }

        explanation.Add($"Minimum {FormatQty(minimum.Value)} {UnitLabel(unit)} aşıldı.");
        return quantity;
    }

    private static (AppliedTariff Tariff, decimal UnitPriceApplied, decimal BaseAmount) ApplyDayThreshold(
        ContractLine line, decimal billableQuantity, List<string> explanation)
    {
        if (line.DayThresholdHours is decimal threshold)
        {
            if (billableQuantity > threshold && line.DailyPrice is decimal dailyPrice)
            {
                explanation.Add($"Gün eşiği ({FormatQty(threshold)} saat) aşıldı, günlük tarife uygulandı.");
                return (AppliedTariff.DAILY, dailyPrice, dailyPrice);
            }

            explanation.Add(line.DailyPrice is null
                ? $"Gün eşiği ({FormatQty(threshold)} saat) tanımlı ama günlük fiyat girilmemiş, eşik yok sayıldı."
                : $"Gün eşiği ({FormatQty(threshold)} saat) aşılmadı, saatlik tarife uygulandı.");
        }

        return (AppliedTariff.HOURLY, line.UnitPrice, billableQuantity * line.UnitPrice);
    }

    private static decimal ApplySurcharges(
        IReadOnlyList<ContractLineSurcharge> surcharges, decimal baseAmount, string currency, List<string> explanation)
    {
        var total = 0m;
        foreach (var surcharge in surcharges.Where(s => s.IsActive))
        {
            var contribution = (surcharge.Multiplier ?? 0m) * baseAmount + (surcharge.FixedAmount ?? 0m);
            if (contribution == 0m)
            {
                continue;
            }

            total += contribution;
            explanation.Add($"{SurchargeLabel(surcharge.SurchargeType)} ek ücreti: {FormatMoney(contribution)} {currency}");
        }

        return total;
    }

    private static string SurchargeLabel(SurchargeType type) => type switch
    {
        SurchargeType.OVERTIME => "Mesai",
        SurchargeType.NIGHT => "Gece",
        SurchargeType.WEEKEND => "Hafta sonu",
        SurchargeType.HOLIDAY => "Resmi tatil",
        _ => type.ToString()
    };

    private static string BuildSnapshot(
        ContractLine line, PricingRequest request, decimal rawQuantity, decimal afterRounding, decimal afterMinimum,
        AppliedTariff appliedTariff, decimal unitPriceApplied, decimal baseAmount, decimal surchargeAmount, decimal lineAmount,
        IReadOnlyList<string> explanation)
    {
        var snapshot = new
        {
            contractLineId = line.ContractLineId,
            unit = line.ServiceCategory.Unit.ToString(),
            unitPrice = line.UnitPrice,
            currency = line.Currency,
            roundingRule = line.RoundingRule.ToString(),
            minBillableQuantity = line.MinBillableQuantity,
            dayThresholdHours = line.DayThresholdHours,
            dailyPrice = line.DailyPrice,
            mobilizationFee = line.MobilizationFee,
            // Bu satır tutarına dahil DEĞİL: sefer başı bedel kayıt seviyesinde bir kez uygulanır.
            mobilizationFeeScope = "RECORD",
            maxQuantityPerRecord = line.MaxQuantityPerRecord,
            rawQuantity,
            afterRounding,
            afterMinimum,
            appliedTariff = appliedTariff.ToString(),
            unitPriceApplied,
            baseAmount,
            surchargeAmount,
            lineAmount,
            appliedSurcharges = request.ApplicableSurcharges
                .Where(s => s.IsActive)
                .Select(s => new { type = s.SurchargeType.ToString(), multiplier = s.Multiplier, fixedAmount = s.FixedAmount }),
            explanation
        };

        return JsonSerializer.Serialize(snapshot);
    }

    private static string UnitLabel(ServiceUnit unit) => UnitLabels.TryGetValue(unit, out var label) ? label : unit.ToString();

    private static string FormatQty(decimal value) => value.ToString("0.####", TrCulture);

    private static string FormatMoney(decimal value) => value.ToString("N2", TrCulture);
}
