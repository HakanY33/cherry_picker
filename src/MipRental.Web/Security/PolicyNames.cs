namespace MipRental.Web.Security;

public static class PolicyNames
{
    public const string MipStaff = "MipStaff";
    public const string FirmUser = "FirmUser";
    public const string CanApprove = "CanApprove";
    public const string CanManageMaster = "CanManageMaster";
    public const string CanManageContract = "CanManageContract";
    public const string CanClosePeriod = "CanClosePeriod";

    // Adim 9 - Fiyat gizliligi: para bilgisini kimin GOREBILECEGI. Ne yapabilir
    // (rol/policy) ile neyi gorebilir (izolasyon/gizlilik) ayri eksenlerdir;
    // bu policy ikincisidir ve hicbir onay/yonetim yetkisi ima etmez.
    public const string CanSeePricing = "CanSeePricing";

    // CLAUDE.md Adım 3 istisnası: Users ekranını ADMIN'in yanı sıra firma
    // adminleri de (sadece kendi firmalarına) yönetebilir; CanManageMaster
    // (salt ADMIN) bu ekran için yeterli değil.
    public const string CanManageUsers = "CanManageUsers";

    // Adım 11 — talep ekranları. Üç aktör, üç ayrı ekran kümesi; her biri kendi
    // policy'siyle sınır çizer. "Ne yapabilir" ekseni; fiyat gizliliği (neyi
    // görebilir) bu ekranlarda hiç devreye girmez çünkü talep ekranlarının
    // HİÇBİRİ tutar döndürmez.
    public const string CanCreateRequest = "CanCreateRequest";

    // Ekipman Müdürlüğü'nün İKİ rolü de listeleri GÖRÜR (EQUIPMENT_VIEWER dahil),
    // ama kararı yalnızca EQUIPMENT_MANAGER verir. Görme ve karar verme ayrı
    // policy: aynı ekranda butonu gizlemek yetmez, POST da düşmeli.
    public const string CanViewEquipmentRequests = "CanViewEquipmentRequests";
    public const string CanDecideEquipmentRequest = "CanDecideEquipmentRequest";

    // FIRM_USER geçiş rolüdür ve RequestStateMachine'de FIRM_MANAGER'a eşdeğer
    // sayılır; policy de aynı ikiliyi kabul eder, yoksa makine izin verdiği hâlde
    // ekran kapalı kalırdı.
    public const string CanManageFirmRequests = "CanManageFirmRequests";

    // Adım 12 — operatör ekranı. "Başladım/Bitirdim" yalnızca FIRM_OPERATOR'ün
    // işidir; RequestStateMachine.EnsureFirmOperator ile aynı rolü ister.
    public const string CanOperateWork = "CanOperateWork";

    // Çalışma kaydını onay zincirine SOKMA yetkisi. FirmUser (yalnızca FirmId
    // claim'i) yetmez: süreyi giren operatör ile hakedişi başlatan kişi aynı
    // olmamalı. Görme ve gönderme ayrı eksenler — ADR-025'in aynı deseni.
    public const string CanSubmitWorkRecord = "CanSubmitWorkRecord";

    // Adım 14 — hakediş. Üç ayrı eksen, ADR-025'in deseni: görmek, yürütmek,
    // karar vermek aynı şey değildir.
    public const string CanViewProgressPayments = "CanViewProgressPayments";
    public const string CanManageProgressPayment = "CanManageProgressPayment";
    public const string CanApproveProgressPayment = "CanApproveProgressPayment";
}
