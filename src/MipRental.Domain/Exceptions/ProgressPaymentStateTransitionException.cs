namespace MipRental.Domain.Exceptions;

// ProgressPaymentStateMachine tarafından fırlatılır: izin verilmeyen geçiş ya da
// boş bırakılan zorunlu red gerekçesi. Yetki hataları için yine
// ApprovalAuthorizationException kullanılır (diğer iki makineyle aynı ayrım).
public sealed class ProgressPaymentStateTransitionException : Exception
{
    public ProgressPaymentStateTransitionException(string message) : base(message)
    {
    }
}
