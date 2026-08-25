namespace MipRental.Domain.Exceptions;

// WorkRecordStateMachine tarafından fırlatılır: izin verilmeyen bir durum geçişi
// denendiğinde, kapalı dönemde geçiş yapılmaya çalışıldığında veya zorunlu gerekçe
// (red / revizyon talebi) boş bırakıldığında.
public sealed class WorkRecordStateTransitionException : Exception
{
    public WorkRecordStateTransitionException(string message) : base(message)
    {
    }
}
