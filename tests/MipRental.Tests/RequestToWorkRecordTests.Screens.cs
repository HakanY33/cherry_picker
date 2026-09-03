using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MipRental.Data;
using MipRental.Data.Services;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;
using MipRental.Web.Common;
using MipRental.Web.Controllers;
using MipRental.Web.Models.WorkRecords;
using MipRental.Web.Security;

namespace MipRental.Tests;

/// <summary>
/// ADIM 12 BÖLÜM B — türetmenin ekranları: operatörün "Bitirdim"i, türeyen
/// taslağın tamamlanması ve gönderim yetkisi.
///
/// Duruş Adım 11 ile aynı: ekranda gizlemek KANIT DEĞİLDİR. Her kural ya
/// veritabanının kendisinde ya da policy'nin gerçekten değerlendirilmesiyle
/// doğrulanır — "buton çizilmedi" bir test sonucu değildir.
/// </summary>
public partial class RequestToWorkRecordTests
{
    /// <summary>
    /// Firma kendi doğrulayıcısını seçemez: alt yüklenicinin seçtiği bir "saha
    /// yetkilisi"nin kanıt değeri olmazdı. Değer talebi açandan gelir — kâğıt
    /// fişi bugün de o imzalıyor.
    /// </summary>
    [Fact]
    public async Task Derive_SetsWitnessedByUser_FromRequester()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection, Local(2026, 9, 15, 8, 0), Local(2026, 9, 15, 12, 0));

        var record = await DeriveAsync(connection, requestId);

        Assert.Equal(RequesterId, record.WitnessedByUserId);
        Assert.Equal(RequesterId, record.RequestedByUserId);
    }

    /// <summary>
    /// B6 — "gönderim bekliyor" haberi firma YETKİLİSİNE düşer. Operatör bu
    /// bildirimi almaz: gönderim onun işi değil ve kaydın mali tarafı ona hiç
    /// yansımaz.
    /// </summary>
    [Fact]
    public async Task Derive_QueuesSubmitNotice_ToFirmManagerOnly()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection, Local(2026, 9, 15, 8, 0), Local(2026, 9, 15, 12, 0));

        await DeriveAsync(connection, requestId);

        await using var db = CreateContext(connection, new FakeCurrentUser());
        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.WorkRecordDerived)
            .ToListAsync();

        Assert.Equal(FirmManagerId, Assert.Single(notifications).UserId);
        Assert.DoesNotContain(notifications, n => n.UserId == FirmOperatorId);
        Assert.Contains("gönderim bekliyor", notifications[0].Body);
    }

    /// <summary>
    /// Türeyen taslak üç alan eksik doğar (Request'te karşılıkları yok) ve bu
    /// yüzden gönderilemez. Form o üçünü yazar — ama YALNIZCA boş olanları:
    /// ikinci gönderim dolu alanı değiştirmez.
    /// </summary>
    [Fact]
    public async Task CompleteDraft_FillsEmptyFieldsOnly_AndIsWriteOnce()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection, Local(2026, 9, 15, 8, 0), Local(2026, 9, 15, 12, 0));
        var recordId = (await DeriveAsync(connection, requestId)).WorkRecordId;

        var manager = FirmManager();
        await using (var db = CreateContext(connection, manager))
        {
            await ApprovalTestFactory.CreateWorkRecordsController(db, manager)
                .CompleteDraft(recordId, 3, " F-1001 ", new DateOnly(2026, 9, 15));
        }

        await using (var db = CreateContext(connection, manager))
        {
            await ApprovalTestFactory.CreateWorkRecordsController(db, manager)
                .CompleteDraft(recordId, 9, "F-9999", new DateOnly(2026, 9, 20));
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var record = await verify.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == recordId);

        Assert.Equal(3, record.PersonnelCount);
        Assert.Equal("F-1001", record.ExternalReceiptNo);
        Assert.Equal(new DateOnly(2026, 9, 15), record.ExternalReceiptDate);
        Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
    }

    /// <summary>
    /// Formda saha yetkilisi ALANI YOK ve ekran MIP personeli listesi
    /// DÖNDÜRMEZ: firma, seçecek bir isim listesi görmez.
    /// </summary>
    [Fact]
    public void CompleteDraft_TakesNoWitnessInput_AndReturnsNoMipStaffList()
    {
        var parameters = typeof(WorkRecordsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == nameof(WorkRecordsController.CompleteDraft))
            .GetParameters()
            .Select(p => p.Name!)
            .ToList();

        Assert.Equal(new[] { "id", "personnelCount", "externalReceiptNo", "externalReceiptDate" }, parameters);

        // Detay modeli tek bir İSİM taşır (kaydın kendi saha yetkilisi); seçilecek
        // kullanıcı LİSTESİ taşımaz — Create ekranındaki gibi bir dropdown yok.
        var listProperties = typeof(WorkRecordDetailsViewModel).GetProperties()
            .Where(p => typeof(IEnumerable<SelectListItem>).IsAssignableFrom(p.PropertyType))
            .ToList();

        Assert.Empty(listProperties);
    }

    /// <summary>Eksikler tamamlanınca kayıt gerçekten gönderilebiliyor: kapanan boşluk buydu.</summary>
    [Fact]
    public async Task CompleteDraft_ThenSubmit_PutsRecordIntoApprovalChain()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection, Local(2026, 9, 15, 8, 0), Local(2026, 9, 15, 12, 0));
        var recordId = (await DeriveAsync(connection, requestId)).WorkRecordId;

        var manager = FirmManager();
        await using (var db = CreateContext(connection, manager))
        {
            var controller = ApprovalTestFactory.CreateWorkRecordsController(db, manager);
            await controller.CompleteDraft(recordId, 3, "F-1001", new DateOnly(2026, 9, 15));
            await controller.Submit(recordId);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var record = await verify.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == recordId);

        Assert.Equal(WorkRecordStatus.PENDING, record.Status);
        Assert.StartsWith("WR-", record.DocumentNo);
    }

    [Theory]
    [InlineData(nameof(WorkRecordsController.Submit))]
    [InlineData(nameof(WorkRecordsController.CompleteDraft))]
    [InlineData(nameof(WorkRecordsController.Cancel))]
    public void SubmissionActions_RequireSubmitPolicy(string actionName)
    {
        var action = typeof(WorkRecordsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == actionName);

        var attribute = Assert.Single(
            action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());

        Assert.Equal(PolicyNames.CanSubmitWorkRecord, attribute.Policy);
    }

    /// <summary>Operatör kaydı GÖRÜR (FirmUser), gönderemez (CanSubmitWorkRecord).</summary>
    [Fact]
    public async Task FirmOperator_SeesRecords_ButFailsSubmitPolicy()
    {
        var authorization = new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthorizationPolicies.AddAppPolicies)
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();

        var operatorPrincipal = Principal(ContractFirmId, RoleNames.FirmOperator);
        var managerPrincipal = Principal(ContractFirmId, RoleNames.FirmManager);

        Assert.True((await authorization.AuthorizeAsync(operatorPrincipal, null, PolicyNames.FirmUser)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(operatorPrincipal, null, PolicyNames.CanSubmitWorkRecord)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(managerPrincipal, null, PolicyNames.CanSubmitWorkRecord)).Succeeded);
    }

    /// <summary>
    /// Policy es geçilse bile durum makinesi operatörü reddeder: POST doğrudan
    /// action'a ulaşsa da kayıt DRAFT kalır. İki katman, ikisi de bağımsız.
    /// </summary>
    [Fact]
    public async Task Submit_ByFirmOperator_IsRejectedOnServer_RecordStaysDraft()
    {
        await using var connection = await CreateSeededConnectionAsync();
        var requestId = await SeedCompletedRequestAsync(connection, Local(2026, 9, 15, 8, 0), Local(2026, 9, 15, 12, 0));
        var recordId = (await DeriveAsync(connection, requestId)).WorkRecordId;

        // Eksik alanlar tamamlanmış olsun ki gönderimi engelleyen tek şey YETKİ olsun.
        var manager = FirmManager();
        await using (var db = CreateContext(connection, manager))
        {
            await ApprovalTestFactory.CreateWorkRecordsController(db, manager)
                .CompleteDraft(recordId, 3, "F-1001", new DateOnly(2026, 9, 15));
        }

        var operatorUser = Operator();
        await using (var db = CreateContext(connection, operatorUser))
        {
            await ApprovalTestFactory.CreateWorkRecordsController(db, operatorUser).Submit(recordId);
        }

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        var record = await verify.WorkRecords.AsNoTracking().SingleAsync(w => w.WorkRecordId == recordId);

        Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
        Assert.StartsWith("DRAFT-", record.DocumentNo);
    }

    /// <summary>
    /// B6 — tek tıkla: talep kapanır, kayıt DRAFT doğar, haber firma
    /// yetkilisine düşer. Operatörün gördüğü mesajda çalışma kaydı GEÇMEZ.
    /// </summary>
    [Fact]
    public async Task Finish_CompletesRequest_DerivesDraft_AndNotifiesFirmManager()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await EnsureNowIsPriceableAsync(connection);
        var requestId = await SeedInProgressRequestAsync(connection);

        var operatorUser = Operator();
        string? message;
        await using (var db = CreateContext(connection, operatorUser))
        {
            var controller = ApprovalTestFactory.CreateFirmOperatorController(db, operatorUser);
            await controller.Finish(requestId);
            message = controller.TempData[TempDataKeys.SuccessMessage] as string;
        }

        Assert.Equal("İş tamamlandı.", message);

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.COMPLETED,
            (await verify.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);

        var record = await verify.WorkRecords.AsNoTracking().SingleAsync(w => w.RequestId == requestId);
        Assert.Equal(WorkRecordStatus.DRAFT, record.Status);
        Assert.Equal(FirmOperatorId, record.EnteredByUserId);

        var notification = Assert.Single(await verify.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.WorkRecordDerived)
            .ToListAsync());
        Assert.Equal(FirmManagerId, notification.UserId);
    }

    /// <summary>
    /// Türetme patlarsa (burada: sözleşmede o varyantın fiyatı yok) iş yine
    /// bitmiştir. Talep COMPLETED kalır, kayıt oluşmaz, sebebi ÇÖZECEK tarafa —
    /// Ekipman Müdürlüğü'ne — bildirim düşer. Operatöre teknik detay yansımaz.
    /// </summary>
    [Fact]
    public async Task Finish_WhenDerivationFails_NotifiesEquipment_AndOperatorSeesNeutralMessage()
    {
        await using var connection = await CreateSeededConnectionAsync();
        await EnsureNowIsPriceableAsync(connection);
        var requestId = await SeedInProgressRequestAsync(connection, variantId: OtherVariantId);

        var operatorUser = Operator();
        string? message;
        await using (var db = CreateContext(connection, operatorUser))
        {
            var controller = ApprovalTestFactory.CreateFirmOperatorController(db, operatorUser);
            await controller.Finish(requestId);
            message = controller.TempData[TempDataKeys.SuccessMessage] as string;
        }

        Assert.Equal("İş tamamlandı.", message);

        await using var verify = CreateContext(connection, new FakeCurrentUser());
        Assert.Equal(RequestStatus.COMPLETED,
            (await verify.Requests.AsNoTracking().SingleAsync(r => r.RequestId == requestId)).Status);
        Assert.False(await verify.WorkRecords.AsNoTracking().AnyAsync(w => w.RequestId == requestId));

        var notification = Assert.Single(await verify.Notifications.AsNoTracking()
            .Where(n => n.TemplateCode == NotificationQueue.Templates.RequestDerivationFailed)
            .ToListAsync());
        Assert.Equal(EquipmentManagerId, notification.UserId);
        Assert.Contains("oluşturulamadı", notification.Body);
    }

    private static FakeCurrentUser FirmManager() =>
        new() { UserId = FirmManagerId, FirmId = ContractFirmId, Roles = { RoleNames.FirmManager } };

    private static ClaimsPrincipal Principal(int? firmId, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        if (firmId is int id)
        {
            claims.Add(new Claim(AppClaimTypes.FirmId, id.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>
    /// "Bitirdim" bitiş saatini SUNUCU saatiyle damgalar; türetme de dönemi ve
    /// fiyatı o tarihe göre arar. Test hangi tarihte koşarsa koşsun anlamlı
    /// kalsın diye ilgili dönemler ve sözleşme aralığı burada garantiye alınır.
    /// </summary>
    private static async Task EnsureNowIsPriceableAsync(SqliteConnection connection)
    {
        await using var db = CreateContext(connection, new FakeCurrentUser());

        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var month in new[] { today.AddMonths(-1), today })
        {
            if (!await db.Periods.AnyAsync(p => p.Year == month.Year && p.Month == month.Month))
            {
                db.Periods.Add(new Period { Year = month.Year, Month = month.Month, Status = PeriodStatus.OPEN });
            }
        }

        var contract = await db.Contracts.SingleAsync(c => c.ContractId == 1);
        contract.StartDate = today.AddYears(-1);
        contract.EndDate = today.AddYears(1);

        var line = await db.ContractLines.SingleAsync(l => l.ContractLineId == 1);
        line.ValidFrom = today.AddYears(-1);

        await db.SaveChangesAsync();
    }

    /// <summary>Operatörün elindeki iş: başlamış, henüz bitmemiş.</summary>
    private static async Task<int> SeedInProgressRequestAsync(SqliteConnection connection, int variantId = VariantId)
    {
        var start = DateTime.UtcNow.AddHours(-2);
        return await SeedCompletedRequestAsync(connection, start, start.AddHours(2),
            requestedDate: DateOnly.FromDateTime(DateTime.Now),
            status: RequestStatus.IN_PROGRESS, variantId: variantId);
    }
}
