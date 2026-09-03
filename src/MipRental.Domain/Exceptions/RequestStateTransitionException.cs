namespace MipRental.Domain.Exceptions;

// RequestStateMachine tarafından fırlatılır: izin verilmeyen bir durum geçişi
// denendiğinde, kapalı dönemde geçiş yapılmaya çalışıldığında veya zorunlu
// gerekçe (red / iptal) boş bırakıldığında.
//
// Yetki hataları için ApprovalAuthorizationException kullanılır — çalışma kaydı
// tarafıyla aynı ayrım: ekran katmanı birine 403, diğerine iş kuralı mesajı
// gösterebilsin.
public sealed class RequestStateTransitionException : Exception
{
    public RequestStateTransitionException(string message) : base(message)
    {
    }
}
