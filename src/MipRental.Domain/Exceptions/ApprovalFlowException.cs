namespace MipRental.Domain.Exceptions;

// Onay zinciri verisi (ApprovalFlows / ApprovalFlowSteps) eksik ya da tutarsız
// olduğunda fırlatılır. CLAUDE.md kural 6: zincir koddan değil veriden okunur —
// veri yoksa sessizce varsayılan bir zincir UYDURULMAZ, net hata verilir.
public sealed class ApprovalFlowException : Exception
{
    public ApprovalFlowException(string message) : base(message)
    {
    }
}
