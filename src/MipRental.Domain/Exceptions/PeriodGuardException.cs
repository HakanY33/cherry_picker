namespace MipRental.Domain.Exceptions;

// PeriodGuardInterceptor tarafından fırlatılır: kapalı döneme kayıt girilmeye
// çalışıldığında veya WorkDate bağlı olduğu Period'un yıl/ay aralığı dışında
// kaldığında. CLAUDE.md kural 4.
public sealed class PeriodGuardException : Exception
{
    public PeriodGuardException(string message) : base(message)
    {
    }
}
