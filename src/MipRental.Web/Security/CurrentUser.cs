using System.Security.Claims;
using MipRental.Domain.Abstractions;

namespace MipRental.Web.Security;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public int UserId =>
        int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public string FullName => Principal?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    // HttpContext yoksa (dotnet ef tasarım-zamanı, background job/hosted service,
    // henüz HTTP isteği bağlamı kurulmamış herhangi bir çağrı) FirmId null döner —
    // yani çağıran taraf bilinçli olarak MIP personeli gibi davranılır (IsMipStaff=true,
    // AppDbContext.HasQueryFilter tüm firmaları gösterir). Gerçek HTTP istekleri için
    // bu asla anonim erişim anlamına gelmez: global [Authorize] filtresi zaten kimliksiz
    // isteği DbContext'e ulaşmadan login'e yönlendirir. Bu davranış sadece
    // request-dışı/arka plan çağrılarda devreye girer ve orada güvenli varsayılan budur.
    public int? FirmId =>
        int.TryParse(Principal?.FindFirstValue(AppClaimTypes.FirmId), out var firmId) ? firmId : null;

    public int? DepartmentId =>
        int.TryParse(Principal?.FindFirstValue(AppClaimTypes.DepartmentId), out var departmentId) ? departmentId : null;

    public bool IsMipStaff => FirmId is null;

    public bool IsFirmUser => FirmId is not null;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
