using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MipRental.Data;
using MipRental.Data.Interceptors;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Controllers;
using MipRental.Web.Models.Requests;
using MipRental.Web.Security;

namespace MipRental.Tests;

/// <summary>
/// ADIM 11 — TALEP EKRANLARI.
///
/// Testlerin ortak duruşu: ekranda gizlemek KANIT DEĞİLDİR. Her kural
/// controller'dan DÖNEN VERİDE ya da veritabanının kendisinde doğrulanır —
/// "buton çizilmedi" değil, "alan modelde yok" / "kayıt değişmedi" / "policy
/// POST'u düşürüyor".
/// </summary>
public class RequestScreensTests
{
    private const int ContractFirmId = 1;      // hizmet için AKTİF sözleşmesi var
    private const int NoContractFirmId = 2;    // sözleşmesi yok
    private const int DepartmentId = 1;
    private const int OtherDepartmentId = 2;
    private const int ServiceId = 1;
    private const int VariantId = 1;
    private const int OtherVariantId = 2;
    private const int LocationId = 1;

    private const int RequesterId = 10;
    private const int OtherRequesterId = 11;
    private const int EquipmentManagerId = 20;
    private const int EquipmentViewerId = 21;
    private const int FirmManagerId = 30;
    private const int OtherFirmManagerId = 31;

    private static readonly DateOnly RequestedDate = new(2026, 9, 15);

    // ---------------------------------------------------------------
    // Kurulum
    // ---------------------------------------------------------------

    private static FakeCurrentUser Requester() =>
        new() { UserId = RequesterId, DepartmentId = DepartmentId, FullName = "Talep Eden", Roles = { RoleNames.Requester } };

    private static FakeCurrentUser OtherRequester() =>
        new() { UserId = OtherRequesterId, DepartmentId = OtherDepartmentId, Roles = { RoleNames.Requester } };

    private static FakeCurrentUser EquipmentManager() =>
        new() { UserId = EquipmentManagerId, Roles = { RoleNames.EquipmentManager } };

    private static FakeCurrentUser EquipmentViewer() =>
        new() { UserId = EquipmentViewerId, Roles = { RoleNames.EquipmentViewer } };

    private static FakeCurrentUser FirmManager() =>
        new() { UserId = FirmManagerId, FirmId = ContractFirmId, Roles = { RoleNames.FirmManager } };

    private static FakeCurrentUser OtherFirmManager() =>
        new() { UserId = OtherFirmManagerId, FirmId = NoContractFirmId, Roles = { RoleNames.FirmManager } };

    private static AppDbContext CreateContext(SqliteConnection connection, ICurrentUser user) =>
        new SqliteTestContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new PeriodGuardInterceptor(), new ImmutabilityGuardInterceptor())
                .Options,
            user);

    private static async Task<SqliteConnection> CreateSeededConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = CreateContext(connection, new FakeCurrentUser());
        await db.Database.EnsureCreatedAsync();

        db.Firms.AddRange(
            new Firm { FirmId = ContractFirmId, Code = "TESTVINC", Title = "Test Vinç Ltd. Şti.", CreatedAt = DateTime.UtcNow },
            new Firm { FirmId = NoContractFirmId, Code = "SOZLESMESIZ", Title = "Sözleşmesiz Firma", CreatedAt = DateTime.UtcNow });

        db.Departments.AddRange(
            new Department { DepartmentId = DepartmentId, Code = "OPS", Name = "Operasyon" },
            new Department { DepartmentId = OtherDepartmentId, Code = "BAKIM", Name = "Bakım" });

        db.Users.AddRange(
            new User { UserId = RequesterId, UserName = "talep1", FullName = "Talep Eden Kullanıcı", Position = "Saha Sorumlusu", DepartmentId = DepartmentId, CreatedAt = DateTime.UtcNow },
            new User { UserId = OtherRequesterId, UserName = "talep2", FullName = "Diğer Talep Eden", DepartmentId = OtherDepartmentId, CreatedAt = DateTime.UtcNow },
            new User { UserId = EquipmentManagerId, UserName = "ekipman1", FullName = "Ekipman Yöneticisi", CreatedAt = DateTime.UtcNow },
            new User { UserId = EquipmentViewerId, UserName = "ekipman2", FullName = "Ekipman Kullanıcısı", CreatedAt = DateTime.UtcNow },
            new User { UserId = FirmManagerId, UserName = "firma1", FullName = "Firma Yetkilisi", FirmId = ContractFirmId, CreatedAt = DateTime.UtcNow },
            new User { UserId = OtherFirmManagerId, UserName = "firma2", FullName = "Diğer Firma Yetkilisi", FirmId = NoContractFirmId, CreatedAt = DateTime.UtcNow });

        db.UserRoles.AddRange(
            new UserRole { UserId = RequesterId, RoleId = 1 },
            new UserRole { UserId = EquipmentManagerId, RoleId = 2 },
            new UserRole { UserId = EquipmentViewerId, RoleId = 8 },
            new UserRole { UserId = FirmManagerId, RoleId = 9 },
            new UserRole { UserId = OtherFirmManagerId, RoleId = 9 });

        db.Locations.Add(new Location { LocationId = LocationId, Name = "İskele 3", FullPath = "Liman > İskele 3" });

        // Hizmet (1), varyantlar (1, 2) ve 2026'nın 12 dönemi model seed'inden
        // (HasData) zaten gelir; burada TEKRAR eklenmez.

        // Sadece 1 numaralı firmanın AKTİF sözleşmesi ve bu hizmet için fiyat satırı var.
        db.Contracts.Add(new Contract
        {
            ContractId = 1,
            FirmId = ContractFirmId,
            ContractNo = "SÖZ-2026-001",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = ContractStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow
        });
        db.ContractLines.Add(new ContractLine
        {
            ContractLineId = 1,
            ContractId = 1,
            ServiceId = ServiceId,
            VariantId = VariantId,
            UnitPrice = 1250m,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return connection;
    }

    /// <summary>Belirli bir durumda, belirli bir kişinin açtığı talep.</summary>
    private static async Task<int> SeedRequestAsync(
        SqliteConnection connection, RequestStatus status,
        int requestedByUserId = RequesterId, int? firmId = null, int departmentId = DepartmentId)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());

        var request = new Request
        {
            DocumentNo = $"CPR-2026-{Guid.NewGuid():N}"[..20],
            Status = status,
            RequestedByUserId = requestedByUserId,
            DepartmentId = departmentId,
            FirmId = firmId,
            IssueDate = new DateOnly(2026, 9, 1),
            RequestedDate = RequestedDate,
            RequestedStartTime = new TimeOnly(8, 0),
            RequestedEndTime = new TimeOnly(12, 0),
            LocationId = LocationId,
            WorkDescription = "Konteyner taşıma",
            AssignedOperatorName = status is RequestStatus.SCHEDULED ? "Ahmet Yılmaz" : null,
            AssignedLicensePlate = status is RequestStatus.SCHEDULED ? "33 ABC 123" : null,
            CreatedAt = DateTime.UtcNow
        };
        request.RequestLines.Add(new RequestLine { LineNo = 1, ServiceId = ServiceId, VariantId = VariantId });

        db.Requests.Add(request);
        await db.SaveChangesAsync();
        return request.RequestId;
    }

    private static IAuthorizationService BuildAuthorizationService() =>
        new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthorizationPolicies.AddAppPolicies)
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal Principal(int? firmId, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        if (firmId is int id)
        {
            claims.Add(new Claim(AppClaimTypes.FirmId, id.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role));
    }

    private static EquipmentRequestsController EquipmentController(SqliteConnection connection, FakeCurrentUser user, string role) =>
        ApprovalTestFactory.CreateEquipmentRequestsController(
            CreateContext(connection, user), user, BuildAuthorizationService(), Principal(null, role));

    // ---------------------------------------------------------------
    // 1) Talep açan SADECE kendi taleplerini görüyor
    // ---------------------------------------------------------------

    [Fact]
    public async Task Requester_OnlySeesOwnRequests()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var mine = await SeedRequestAsync(connection, RequestStatus.PENDING_EQUIPMENT);
        await SeedRequestAsync(connection, RequestStatus.PENDING_EQUIPMENT, requestedByUserId: OtherRequesterId, departmentId: OtherDepartmentId);

        var user = Requester();
        var controller = ApprovalTestFactory.CreateRequestsController(CreateContext(connection, user), user);

        var model = Assert.IsType<MyRequestsViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);

        Assert.Equal(mine, Assert.Single(model.Items).RequestId);
    }

    /// <summary>Id'yi elle yazmak da işe yaramaz: sahiplik sorgunun kendisinde.</summary>
    [Fact]
    public async Task Requester_CannotOpenAnotherPersonsRequestById()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var otherRequestId = await SeedRequestAsync(
            connection, RequestStatus.PENDING_EQUIPMENT, requestedByUserId: OtherRequesterId, departmentId: OtherDepartmentId);

        var user = Requester();
        var controller = ApprovalTestFactory.CreateRequestsController(CreateContext(connection, user), user);

        Assert.IsType<NotFoundResult>(await controller.Details(otherRequestId));
    }

    // ---------------------------------------------------------------
    // 2) Başka departman adına talep açılamıyor (tampered POST)
    // ---------------------------------------------------------------

    /// <summary>
    /// Yapısal kanıt: form modelinde DepartmentId diye bir alan YOK, dolayısıyla
    /// POST'a eklense bile bağlanacak bir yer bulunmaz.
    /// </summary>
    [Fact]
    public void RequestForm_HasNoDepartmentField()
    {
        Assert.Null(typeof(RequestFormViewModel).GetProperty("DepartmentId"));
        Assert.Null(typeof(RequestFormViewModel).GetProperty("RequestedByUserId"));
    }

    /// <summary>
    /// Davranışsal kanıt: POST'a fazladan alan konsa dahi kaydedilen departman
    /// OTURUMDAN gelir.
    /// </summary>
    [Fact]
    public async Task Requester_CannotCreateRequestForAnotherDepartment()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var user = Requester();
        var controller = ApprovalTestFactory.CreateRequestsController(CreateContext(connection, user), user);

        await controller.Create(NewRequestForm(), action: "draft");

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var created = await db.Requests.AsNoTracking().SingleAsync();

        Assert.Equal(DepartmentId, created.DepartmentId);
        Assert.Equal(RequesterId, created.RequestedByUserId);
        Assert.NotEqual(OtherDepartmentId, created.DepartmentId);
    }

    [Fact]
    public async Task Requester_SubmittingRequest_GetsDocumentNumberAndGoesToEquipment()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var user = Requester();
        var controller = ApprovalTestFactory.CreateRequestsController(CreateContext(connection, user), user);

        await controller.Create(NewRequestForm(), action: "submit");

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var created = await db.Requests.AsNoTracking().SingleAsync();

        Assert.Equal(RequestStatus.PENDING_EQUIPMENT, created.Status);
        Assert.StartsWith("CPR-2026-", created.DocumentNo);
        Assert.NotNull(created.SubmittedAt);
    }

    /// <summary>Gönderimde zorunlu alan kontrolü; taslakta değil.</summary>
    [Fact]
    public async Task Requester_SubmittingIncompleteRequest_IsRejectedButDraftIsAccepted()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var user = Requester();
        var controller = ApprovalTestFactory.CreateRequestsController(CreateContext(connection, user), user);

        var incomplete = new RequestFormViewModel { RequestedDate = RequestedDate };
        Assert.IsType<ViewResult>(await controller.Create(incomplete, action: "submit"));
        Assert.False(controller.ModelState.IsValid);

        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            Assert.Empty(await db.Requests.AsNoTracking().ToListAsync());
        }

        var draftUser = Requester();
        var draftController = ApprovalTestFactory.CreateRequestsController(CreateContext(connection, draftUser), draftUser);
        Assert.IsType<RedirectToActionResult>(
            await draftController.Create(new RequestFormViewModel { RequestedDate = RequestedDate }, action: "draft"));

        await using var after = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.DRAFT, (await after.Requests.AsNoTracking().SingleAsync()).Status);
    }

    // ---------------------------------------------------------------
    // 3) Ekipman Müdürlüğü lokasyon / iş tanımı DEĞİŞTİREMİYOR
    // ---------------------------------------------------------------

    [Fact]
    public void EquipmentApprovalModel_HasNoLocationOrDescriptionField()
    {
        var model = typeof(EquipmentApprovalModel);

        Assert.Null(model.GetProperty("LocationId"));
        Assert.Null(model.GetProperty("LocationText"));
        Assert.Null(model.GetProperty("WorkDescription"));
        Assert.Null(model.GetProperty("RequestedByUserId"));
        Assert.Null(model.GetProperty("DepartmentId"));
    }

    [Fact]
    public async Task EquipmentManager_ApprovingRequest_CannotChangeLocationOrDescription()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_EQUIPMENT);

        var controller = EquipmentController(connection, EquipmentManager(), RoleNames.EquipmentManager);

        // Saat düzenlenip firma atanıyor — modelde lokasyon/iş tanımı YOK.
        await controller.Approve(requestId, new EquipmentApprovalModel
        {
            RequestedDate = RequestedDate,
            RequestedStartTime = new TimeOnly(10, 0),
            VariantId = VariantId,
            FirmId = ContractFirmId
        });

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var after = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(LocationId, after.LocationId);
        Assert.Equal("Konteyner taşıma", after.WorkDescription);

        // Düzenlemesine izin verilen alan gerçekten değişti; test aşırı kısıtlamayı da yakalar.
        Assert.Equal(new TimeOnly(10, 0), after.RequestedStartTime);
        Assert.Equal(RequestStatus.PENDING_FIRM, after.Status);
        Assert.Equal(ContractFirmId, after.FirmId);
    }

    // ---------------------------------------------------------------
    // 4) Firma seçim listesi: yalnızca aktif sözleşmesi olan firmalar
    // ---------------------------------------------------------------

    [Fact]
    public async Task FirmOptions_ContainOnlyFirmsWithActiveContractForTheService()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_EQUIPMENT);

        var controller = EquipmentController(connection, EquipmentManager(), RoleNames.EquipmentManager);
        var model = Assert.IsType<EquipmentRequestDetailsViewModel>(
            Assert.IsType<ViewResult>(await controller.Details(requestId)).Model);

        Assert.Equal(ContractFirmId.ToString(), Assert.Single(model.FirmOptions).Value);
    }

    /// <summary>
    /// Liste ekranda sınırlı ama sunucuda TEKRAR doğrulanır: elle kurulmuş bir
    /// POST sözleşmesiz firmaya yönlendiremez.
    /// </summary>
    [Fact]
    public async Task EquipmentManager_CannotRouteRequestToFirmWithoutContract()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_EQUIPMENT);

        var controller = EquipmentController(connection, EquipmentManager(), RoleNames.EquipmentManager);

        await controller.Approve(requestId, new EquipmentApprovalModel
        {
            RequestedDate = RequestedDate,
            RequestedStartTime = new TimeOnly(8, 0),
            VariantId = VariantId,
            FirmId = NoContractFirmId
        });

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var after = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(RequestStatus.PENDING_EQUIPMENT, after.Status);
        Assert.Null(after.FirmId);
    }

    // ---------------------------------------------------------------
    // 5) Firma yetkilisi BAŞKA firmaya yönlendirilmiş talebi göremiyor
    // ---------------------------------------------------------------

    [Fact]
    public async Task FirmManager_DoesNotSeeAnotherFirmsRequest()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var mine = await SeedRequestAsync(connection, RequestStatus.PENDING_FIRM, firmId: ContractFirmId);
        var theirs = await SeedRequestAsync(connection, RequestStatus.PENDING_FIRM, firmId: NoContractFirmId);

        var user = FirmManager();
        var controller = ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, user), user);

        var model = Assert.IsType<FirmRequestsViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);
        Assert.Equal(mine, Assert.Single(model.Items).RequestId);

        // Id elle yazılsa da sorgu boş döner (global query filter, kural 7).
        Assert.IsType<NotFoundResult>(await controller.Accept(theirs));
    }

    // ---------------------------------------------------------------
    // 6) Firma yetkilisi talep edenin KİMLİK bilgilerini göremiyor
    // ---------------------------------------------------------------

    /// <summary>
    /// Yapısal kanıt: firma modellerinde talep edenin kimliğine dair bir alan
    /// HİÇ YOK. "null geliyor" yetmez — alan bulunmamalı.
    /// </summary>
    [Theory]
    [InlineData(typeof(FirmRequestRow))]
    [InlineData(typeof(FirmRequestAcceptViewModel))]
    [InlineData(typeof(FirmRequestsViewModel))]
    public void FirmModels_CarryNoRequesterIdentity(Type type)
    {
        foreach (var forbidden in new[]
                 {
                     "RequesterName", "RequesterPosition", "DepartmentName", "DepartmentId",
                     "RequestedByUserId", "RequestedByUser"
                 })
        {
            Assert.Null(type.GetProperty(forbidden));
        }
    }

    [Fact]
    public async Task FirmManager_AcceptScreen_ReturnsNoRequesterIdentityValue()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_FIRM, firmId: ContractFirmId);

        var user = FirmManager();
        var controller = ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, user), user);
        var model = Assert.IsType<FirmRequestAcceptViewModel>(
            Assert.IsType<ViewResult>(await controller.Accept(requestId)).Model);

        // İşi yapabilmesi için gereken her şey var...
        Assert.Equal(RequestedDate, model.RequestedDate);
        Assert.Equal("Liman > İskele 3", model.LocationDisplay);
        Assert.Equal("Konteyner taşıma", model.WorkDescription);

        // ...ama modelin HİÇBİR alanında MIP personelinin adı geçmiyor.
        Assert.DoesNotContain(PropertyValues(model), v => v.Contains("Talep Eden", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------
    // 7) Operatör/plaka girilmeden kabul edilemiyor
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null, "33 ABC 123")]
    [InlineData("   ", "33 ABC 123")]
    [InlineData("Ahmet Yılmaz", null)]
    [InlineData("Ahmet Yılmaz", "  ")]
    public async Task FirmManager_CannotAcceptWithoutOperatorAndPlate(string? operatorName, string? licensePlate)
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_FIRM, firmId: ContractFirmId);

        var user = FirmManager();
        var controller = ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, user), user);

        await controller.Accept(requestId, operatorName, licensePlate);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var after = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(RequestStatus.PENDING_FIRM, after.Status);
    }

    [Fact]
    public async Task FirmManager_AcceptingWithOperatorAndPlate_SchedulesTheJob()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_FIRM, firmId: ContractFirmId);

        var user = FirmManager();
        var controller = ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, user), user);

        await controller.Accept(requestId, "Ahmet Yılmaz", "33 ABC 123");

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var after = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(RequestStatus.SCHEDULED, after.Status);
        Assert.Equal("Ahmet Yılmaz", after.AssignedOperatorName);
        Assert.Equal("33 ABC 123", after.AssignedLicensePlate);
        Assert.NotNull(after.FirmDecisionAt);
    }

    // ---------------------------------------------------------------
    // 8) Gerekçesiz red REDDEDİLİYOR
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EquipmentManager_CannotRejectWithoutReason(string? reason)
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_EQUIPMENT);

        var controller = EquipmentController(connection, EquipmentManager(), RoleNames.EquipmentManager);
        await controller.Reject(requestId, reason);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var after = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal(RequestStatus.PENDING_EQUIPMENT, after.Status);
        Assert.Null(after.RejectionReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task FirmManager_CannotRejectWithoutReason(string? reason)
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_FIRM, firmId: ContractFirmId);

        var user = FirmManager();
        var controller = ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, user), user);
        await controller.Reject(requestId, reason);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.PENDING_FIRM,
            (await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Requester_CannotCancelWithoutReason(string? reason)
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.DRAFT);

        var user = Requester();
        var controller = ApprovalTestFactory.CreateRequestsController(CreateContext(connection, user), user);
        await controller.Cancel(requestId, reason);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.DRAFT,
            (await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
    }

    // ---------------------------------------------------------------
    // 9) EQUIPMENT_VIEWER karar VEREMİYOR — POST denemesi de engelleniyor
    // ---------------------------------------------------------------

    /// <summary>
    /// Ekranda buton gizlenmesi yetmez; karar action'ları ayrı bir policy ile
    /// KAPALIDIR. Attribute'un varlığı testle sabitlenir.
    /// </summary>
    [Theory]
    [InlineData(nameof(EquipmentRequestsController.Approve))]
    [InlineData(nameof(EquipmentRequestsController.Reject))]
    public void EquipmentDecisionActions_RequireDecisionPolicy(string actionName)
    {
        var action = typeof(EquipmentRequestsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == actionName);

        var attribute = Assert.Single(
            action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());

        Assert.Equal(PolicyNames.CanDecideEquipmentRequest, attribute.Policy);
    }

    [Fact]
    public async Task EquipmentViewer_CanSeeListsButFailsDecisionPolicy()
    {
        var auth = BuildAuthorizationService();
        var viewer = Principal(null, RoleNames.EquipmentViewer);

        Assert.True((await auth.AuthorizeAsync(viewer, null, PolicyNames.CanViewEquipmentRequests)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(viewer, null, PolicyNames.CanDecideEquipmentRequest)).Succeeded);
    }

    /// <summary>Salt okuyan kullanıcıya karar butonu ÇİZİLMEZ (modeldeki bayrak false).</summary>
    [Fact]
    public async Task EquipmentViewer_GetsNoDecideFlag()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await SeedRequestAsync(connection, RequestStatus.PENDING_EQUIPMENT);

        var viewerModel = Assert.IsType<EquipmentRequestsViewModel>(Assert.IsType<ViewResult>(
            await EquipmentController(connection, EquipmentViewer(), RoleNames.EquipmentViewer).Index()).Model);
        Assert.False(viewerModel.CanDecide);
        Assert.NotEmpty(viewerModel.Items); // listeyi görüyor, sadece karar veremiyor

        var managerModel = Assert.IsType<EquipmentRequestsViewModel>(Assert.IsType<ViewResult>(
            await EquipmentController(connection, EquipmentManager(), RoleNames.EquipmentManager).Index()).Model);
        Assert.True(managerModel.CanDecide);
    }

    /// <summary>
    /// Policy es geçilse bile durum makinesi EQUIPMENT_VIEWER'ı reddeder:
    /// yetki iki katmanlı ve ikisi de bağımsız çalışır.
    /// </summary>
    [Fact]
    public async Task EquipmentViewer_ReachingTheActionDirectly_StillCannotApprove()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_EQUIPMENT);

        var controller = EquipmentController(connection, EquipmentViewer(), RoleNames.EquipmentViewer);
        await controller.Approve(requestId, new EquipmentApprovalModel
        {
            RequestedDate = RequestedDate,
            VariantId = VariantId,
            FirmId = ContractFirmId
        });

        await using var db = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.PENDING_EQUIPMENT,
            (await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
    }

    // ---------------------------------------------------------------
    // 10) HİÇBİR talep ekranında tutar dönmüyor
    // ---------------------------------------------------------------

    /// <summary>
    /// Adım 9'un kuralı talep ekranlarında da geçerli — hatta daha katı: para
    /// alanı "yetkisizde null" değil, TİPİN KENDİSİNDE YOK.
    /// </summary>
    [Fact]
    public void RequestViewModels_HaveNoMonetaryField()
    {
        var types = typeof(RequestFormViewModel).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(RequestFormViewModel).Namespace)
            .ToList();

        Assert.NotEmpty(types);

        foreach (var property in types.SelectMany(t => t.GetProperties()))
        {
            Assert.False(PricingFields.IsMoney(property.Name),
                $"{property.DeclaringType!.Name}.{property.Name} bir para alanı; talep ekranları tutar döndürmez.");

            // Ad listesinde olmayan yeni bir para alanı da sızmasın: talep
            // modellerinde decimal tipinde tek meşru alan tahmini süredir.
            var isDecimal = property.PropertyType == typeof(decimal) || property.PropertyType == typeof(decimal?);
            Assert.False(isDecimal && property.Name != "EstimatedHours",
                $"{property.DeclaringType!.Name}.{property.Name} decimal; para alanı olmadığı doğrulanmalı.");
        }
    }

    [Fact]
    public async Task RequestScreens_ReturnNoAmountAnywhere()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.SCHEDULED, firmId: ContractFirmId);

        var requester = Requester();
        var requesterModel = Assert.IsType<RequestDetailsViewModel>(Assert.IsType<ViewResult>(
            await ApprovalTestFactory.CreateRequestsController(CreateContext(connection, requester), requester)
                .Details(requestId)).Model);

        var equipmentModel = Assert.IsType<EquipmentRequestDetailsViewModel>(Assert.IsType<ViewResult>(
            await EquipmentController(connection, EquipmentManager(), RoleNames.EquipmentManager)
                .Details(requestId)).Model);

        var firmUser = FirmManager();
        var firmModel = Assert.IsType<FirmRequestAcceptViewModel>(Assert.IsType<ViewResult>(
            await ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, firmUser), firmUser)
                .Accept(requestId)).Model);

        // Sözleşmedeki birim fiyat 1250; üç modelin hiçbirinde geçmiyor.
        foreach (var model in new object[] { requesterModel, equipmentModel, firmModel })
        {
            Assert.DoesNotContain(PropertyValues(model), v => v.Contains("1250") || v.Contains("1.250"));
        }
    }

    // ---------------------------------------------------------------
    // 11) Durum geçişleri RequestStateMachine üzerinden
    // ---------------------------------------------------------------

    /// <summary>
    /// Controller'da "Status = ..." DOĞRUDAN ATAMASI olmamalı. Bunu davranışla
    /// kanıtlamak mümkün değil (aynı sonucu üreten iki yol vardır), bu yüzden
    /// KAYNAK KODU taranır — Adım 7'de çalışma kaydı tarafına konan kuralın
    /// talep tarafındaki karşılığı.
    /// </summary>
    [Theory]
    [InlineData("RequestsController.cs")]
    [InlineData("EquipmentRequestsController.cs")]
    [InlineData("FirmRequestsController.cs")]
    public void RequestControllers_NeverAssignStatusDirectly(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "MipRental.Web", "Controllers", fileName);
        Assert.True(File.Exists(path), $"Kaynak dosya bulunamadı: {path}");

        var source = File.ReadAllText(path);

        // "entity.Status = ..." yasak. Karşılaştırma (== ) ve view model
        // projeksiyonundaki "Status = r.Status" (noktasız, atama değil eşleme)
        // meşrudur; desen tam olarak NOKTALI ATAMAYI arar.
        var assignment = System.Text.RegularExpressions.Regex.Match(source, @"\.Status\s*=(?!=)");
        Assert.False(assignment.Success,
            $"{fileName} içinde doğrudan Status ataması var: \"{Excerpt(source, assignment.Index)}\". " +
            "Geçişler RequestStateMachine üzerinden yapılmalı.");

        // Ters yönden kanıt: geçişler gerçekten makineden geçiyor.
        Assert.Contains("RequestStateMachine.", source);
    }

    private static string Excerpt(string source, int index) =>
        source.Substring(Math.Max(0, index - 30), Math.Min(60, source.Length - Math.Max(0, index - 30)))
              .ReplaceLineEndings(" ");

    /// <summary>Uçtan uca: talep açılır, ekipman onaylar, firma kabul eder.</summary>
    [Fact]
    public async Task FullFlow_DraftToScheduled_WalksThroughEveryActor()
    {
        await using var connection = await CreateSeededConnectionAsync();

        var requester = Requester();
        await ApprovalTestFactory.CreateRequestsController(CreateContext(connection, requester), requester)
            .Create(NewRequestForm(), action: "submit");

        int requestId;
        await using (var db = CreateContext(connection, new FakeCurrentUser()))
        {
            requestId = (await db.Requests.AsNoTracking().SingleAsync()).RequestId;
        }

        await EquipmentController(connection, EquipmentManager(), RoleNames.EquipmentManager)
            .Approve(requestId, new EquipmentApprovalModel
            {
                RequestedDate = RequestedDate,
                RequestedStartTime = new TimeOnly(9, 0),
                VariantId = VariantId,
                FirmId = ContractFirmId
            });

        var firmUser = FirmManager();
        await ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, firmUser), firmUser)
            .Accept(requestId, "Ahmet Yılmaz", "33 ABC 123");

        await using var final = CreateContext(connection, new FakeCurrentUser());
        var request = await final.Requests.AsNoTracking().SingleAsync();

        Assert.Equal(RequestStatus.SCHEDULED, request.Status);
        Assert.Equal(ContractFirmId, request.FirmId);
        Assert.Equal(new TimeOnly(9, 0), request.RequestedStartTime);
        Assert.NotNull(request.EquipmentDecisionAt);
        Assert.NotNull(request.FirmDecisionAt);

        // Her durum geçişinde bildirim kuyruğa düştü (mail GÖNDERİLMEDİ).
        var notifications = await final.Notifications.AsNoTracking()
            .Where(n => n.DocumentType == DocumentType.REQUEST && n.DocumentId == requestId)
            .ToListAsync();
        Assert.All(notifications, n => Assert.Equal(NotificationStatus.QUEUED, n.Status));
        Assert.Contains(notifications, n => n.TemplateCode == NotificationQueueTemplates.EquipmentApproved);
        Assert.Contains(notifications, n => n.TemplateCode == NotificationQueueTemplates.FirmAccepted);
    }

    // ---------------------------------------------------------------
    // Madde 0 — SCHEDULED'da operatör/plaka değişikliği
    // ---------------------------------------------------------------

    [Fact]
    public async Task FirmManager_CanChangeOperatorAndPlateOnScheduledJob()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.SCHEDULED, firmId: ContractFirmId);

        var user = FirmManager();
        var controller = ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, user), user);

        await controller.UpdateAssignment(requestId, "Mehmet Demir", "33 XYZ 987");

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var after = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal("Mehmet Demir", after.AssignedOperatorName);
        Assert.Equal("33 XYZ 987", after.AssignedLicensePlate);

        // DURUM DEĞİŞMEDİ: bu bir geçiş değil, atamanın güncellenmesi.
        Assert.Equal(RequestStatus.SCHEDULED, after.Status);
    }

    [Fact]
    public async Task AssignmentUpdate_CannotBlankOutOperatorOrPlate()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.SCHEDULED, firmId: ContractFirmId);

        var user = FirmManager();
        var controller = ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, user), user);

        await controller.UpdateAssignment(requestId, "   ", "33 XYZ 987");

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var after = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Equal("Ahmet Yılmaz", after.AssignedOperatorName);
    }

    [Fact]
    public async Task AssignmentUpdate_OnRequestThatIsNotScheduled_IsRejected()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedRequestAsync(connection, RequestStatus.PENDING_FIRM, firmId: ContractFirmId);

        var user = FirmManager();
        var controller = ApprovalTestFactory.CreateFirmRequestsController(CreateContext(connection, user), user);

        await controller.UpdateAssignment(requestId, "Mehmet Demir", "33 XYZ 987");

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var after = await db.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId);

        Assert.Null(after.AssignedOperatorName);
        Assert.Equal(RequestStatus.PENDING_FIRM, after.Status);
    }

    // ---------------------------------------------------------------
    // Yardımcılar
    // ---------------------------------------------------------------

    private static RequestFormViewModel NewRequestForm() => new()
    {
        RequestedDate = RequestedDate,
        RequestedStartTime = new TimeOnly(8, 0),
        EstimatedHours = 4,
        LocationId = LocationId,
        WorkDescription = "Konteyner taşıma",
        ServiceId = ServiceId,
        VariantId = VariantId
    };

    /// <summary>Modelin tüm string'e çevrilebilir alan değerleri — sızıntı taraması için.</summary>
    private static List<string> PropertyValues(object model) =>
        model.GetType().GetProperties()
            .Select(p => p.GetValue(model)?.ToString())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList()!;

    /// <summary>Test derlemesinden yukarı doğru çözüm dosyasını arayarak repo kökü.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MipRental.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>NotificationQueue.Templates iç içe sınıf olduğu için kısayol.</summary>
    private static class NotificationQueueTemplates
    {
        public const string EquipmentApproved = "REQ_EQUIPMENT_APPROVED";
        public const string FirmAccepted = "REQ_FIRM_ACCEPTED";
    }
}
