using MipRental.Domain.Entities;

namespace MipRental.Domain.Approvals;

// Bir belgeye uygulanacak onay zinciri: hangi akış seçildi ve o akışın hangi
// adımları BU kayıt için geçerli. AmountThreshold ile elenen adımlar burada YOKTUR.
public sealed class ApprovalChain
{
    public required ApprovalFlow Flow { get; init; }

    // StepNo sırasında, tutar eşiğine göre süzülmüş adımlar.
    public required IReadOnlyList<ApprovalFlowStep> Steps { get; init; }

    public ApprovalFlowStep First => Steps[0];

    // Verilen adımdan SONRAKİ adım (yoksa null). "Sonraki" sırayı StepNo değil,
    // süzülmüş listedeki konum belirler: eşiğin altında kalan adımlar atlanır.
    public ApprovalFlowStep? StepAfter(int stepNo)
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            if (Steps[i].StepNo == stepNo)
            {
                return i + 1 < Steps.Count ? Steps[i + 1] : null;
            }
        }

        return null;
    }

    public ApprovalFlowStep? StepByNo(int stepNo) => Steps.FirstOrDefault(s => s.StepNo == stepNo);
}
