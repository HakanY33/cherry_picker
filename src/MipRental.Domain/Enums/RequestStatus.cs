namespace MipRental.Domain.Enums;

/// <summary>
/// Talep durumları (Adım 10). Çalışma kaydının durumlarından AYRIDIR: talep
/// iş ÖNCESİ, çalışma kaydı iş SONRASI yaşar (bkz. ADR-011).
///
/// Adım 10'dan önce bu enum çalışma kaydınınkinin kopyasıydı
/// (PENDING/APPROVED/REJECTED/REVISION_REQUESTED). Talep akışında onay iki ayrı
/// tarafta olduğu için tek bir PENDING/REJECTED yetmiyor: "kim bekletiyor" ve
/// "kim reddetti" durumun kendisinden okunabilmeli.
/// </summary>
public enum RequestStatus
{
    DRAFT,
    SUBMITTED,
    PENDING_EQUIPMENT,
    PENDING_FIRM,
    SCHEDULED,
    IN_PROGRESS,
    COMPLETED,

    // Adım 16 — süre teyidi. COMPLETED artık terminal DEĞİL: operatörün
    // "bitirdim" dediği an işin GERÇEKLEŞEN süresi henüz kimse tarafından
    // teyit edilmemiştir. Teyidi, bugün kâğıt fişi imzalayan kişi verir:
    // talebi açan. CONFIRMED terminaldir ve çalışma kaydı ondan türer.
    CONFIRMED,

    // Talep açan süreye itiraz etti. Hakem Ekipman Müdürlüğü'dür: saatleri
    // düzeltip onaylar ya da "faturalanmayacak" deyip iptal eder.
    DISPUTED,

    REJECTED_BY_EQUIPMENT,
    REJECTED_BY_FIRM,
    CANCELLED
}
