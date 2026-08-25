namespace MipRental.Domain.Exceptions;

// ImmutabilityGuardInterceptor tarafından fırlatılır: onaylanmış bir mali kayıt
// (veya herhangi bir kayıt) silinmeye ya da izin verilmeyen bir alanı
// güncellenmeye çalışıldığında. CLAUDE.md kural 1.
public sealed class ImmutabilityViolationException : Exception
{
    public ImmutabilityViolationException(string message) : base(message)
    {
    }
}
