namespace MipRental.Domain.Exceptions;

// Geçiş kendi başına izinli olsa bile KULLANICININ o geçişi yapma yetkisi yoksa
// fırlatılır (yanlış rol, başka firmanın kaydı, alt yüklenicinin kendi kaydını
// onaylamaya çalışması). Yetki hatası ile kural hatası ayrı tutulur ki ekran
// katmanı birine 403, diğerine iş kuralı mesajı gösterebilsin.
public sealed class ApprovalAuthorizationException : Exception
{
    public ApprovalAuthorizationException(string message) : base(message)
    {
    }
}
