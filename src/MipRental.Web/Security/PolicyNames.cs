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
}
