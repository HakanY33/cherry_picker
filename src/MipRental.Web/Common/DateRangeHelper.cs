namespace MipRental.Web.Common;

public static class DateRangeHelper
{
    /// <summary>
    /// İki tarih aralığı çakışıyor mu? Bitiş tarihi null ise süresiz (açık uçlu) kabul edilir.
    /// </summary>
    public static bool Overlaps(DateOnly aFrom, DateOnly? aTo, DateOnly bFrom, DateOnly? bTo)
    {
        var aEnd = aTo ?? DateOnly.MaxValue;
        var bEnd = bTo ?? DateOnly.MaxValue;
        return aFrom <= bEnd && bFrom <= aEnd;
    }
}
