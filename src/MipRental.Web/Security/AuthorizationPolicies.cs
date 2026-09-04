using Microsoft.AspNetCore.Authorization;

namespace MipRental.Web.Security;

/// <summary>
/// Uygulamanın yetki politikaları. Program.cs'ten ayrı bir sınıfta durmasının
/// sebebi TEST EDİLEBİLİRLİK: "firma kullanıcısı sözleşme ekranına giremez" gibi
/// kurallar, controller'daki attribute'un adına bakarak değil politikayı gerçekten
/// değerlendirerek doğrulanabilsin.
/// </summary>
public static class AuthorizationPolicies
{
    public static void AddAppPolicies(AuthorizationOptions options)
    {
        options.AddPolicy(PolicyNames.MipStaff, policy =>
            policy.RequireAssertion(ctx => !ctx.User.HasClaim(c => c.Type == AppClaimTypes.FirmId)));

        options.AddPolicy(PolicyNames.FirmUser, policy =>
            policy.RequireClaim(AppClaimTypes.FirmId));

        // Adım 10: SUPERVISOR/DEPT_HEAD yerine EQUIPMENT_MANAGER/BUDGET_MANAGER.
        // EQUIPMENT_VIEWER salt okurdur, onaylamaz.
        options.AddPolicy(PolicyNames.CanApprove, policy =>
            policy.RequireRole(RoleNames.EquipmentManager, RoleNames.BudgetManager));

        options.AddPolicy(PolicyNames.CanManageMaster, policy =>
            policy.RequireRole(RoleNames.Admin));

        // Sözleşme ekranları birim fiyat gösterir; bu yüzden fiyat görebilen
        // rollerle sınırlıdır (Adım 9, kapatılan açık 4.1).
        options.AddPolicy(PolicyNames.CanManageContract, policy =>
            policy.RequireRole(RoleNames.Admin, RoleNames.Budget));

        options.AddPolicy(PolicyNames.CanClosePeriod, policy =>
            policy.RequireRole(RoleNames.Budget));

        // Para bilgisini yalnızca bu roller görür. Ekipman Müdürlüğü'nün İKİ rolü de
        // (EQUIPMENT_MANAGER, EQUIPMENT_VIEWER) ve firma rolleri HARİÇ — onaylamak
        // ile fiyat görmek ayrı eksenlerdir (Adım 9). Muhasebe (ACCOUNTING) DAHİL:
        // işi alt yüklenici e-faturasını maliyet tablosuyla karşılaştırmaktır.
        // Tek doğru kaynak: CurrentUser.CanSeePricing aynı rol listesini kullanır;
        // ikisi birlikte değiştirilir.
        options.AddPolicy(PolicyNames.CanSeePricing, policy =>
            policy.RequireRole(RoleNames.Budget, RoleNames.BudgetManager, RoleNames.Admin, RoleNames.Accounting));

        // ---------------------------------------------------------------
        // Adım 11 — talep ekranları.
        //
        // Policy'ler RequestStateMachine'in rol kontrolleriyle BİREBİR aynı
        // rolleri ister. İkisi de gereklidir ve biri diğerinin yerine geçmez:
        // policy ekranın/POST'un kapısını tutar, makine kaydın kendisini korur
        // (yanlış durumda, yanlış firmada, kapalı dönemde işlem yapılamaz).
        // ---------------------------------------------------------------
        options.AddPolicy(PolicyNames.CanCreateRequest, policy =>
            policy.RequireRole(RoleNames.Requester));

        // EQUIPMENT_VIEWER listeleri ve detayı GÖRÜR...
        options.AddPolicy(PolicyNames.CanViewEquipmentRequests, policy =>
            policy.RequireRole(RoleNames.EquipmentManager, RoleNames.EquipmentViewer));

        // ...ama karar veremez. Ekranda butonu gizlemek yetmez: onay/red
        // action'ları da bu policy ile kapalıdır.
        options.AddPolicy(PolicyNames.CanDecideEquipmentRequest, policy =>
            policy.RequireRole(RoleNames.EquipmentManager));

        // FIRM_USER, RequestStateMachine.EnsureFirmManager'da FIRM_MANAGER'a
        // eşdeğer sayılan geçiş rolüdür; policy de aynı ikiliyi kabul eder.
        options.AddPolicy(PolicyNames.CanManageFirmRequests, policy =>
            policy.RequireRole(RoleNames.FirmManager, RoleNames.FirmUser));

        // ---------------------------------------------------------------
        // Adım 12 — operatör ekranı ve gönderim yetkisi.
        // ---------------------------------------------------------------

        // Operatör ekranı: makinedeki EnsureFirmOperator ile aynı rol.
        options.AddPolicy(PolicyNames.CanOperateWork, policy =>
            policy.RequireRole(RoleNames.FirmOperator));

        // Gönderim FIRM_OPERATOR'e KAPALI. Operatör kaydı görür (liste ve detay
        // FirmUser'a açık), gönderemez: işi yapan ile mali talebi zincire sokan
        // aynı kişi olursa gerçekleşen süreyi teyit eden kimse kalmaz (ADR-028).
        // Aynı rol ikilisi RequestStateMachine.EnsureFirmManager'da da geçerli;
        // FIRM_USER geçiş rolü olarak FIRM_MANAGER'a eşdeğer sayılır.
        options.AddPolicy(PolicyNames.CanSubmitWorkRecord, policy =>
            policy.RequireRole(RoleNames.FirmManager, RoleNames.FirmUser));

        // ---------------------------------------------------------------
        // Adım 14 — hakediş.
        //
        // Hakediş ekranları TUTAR gösterir; bu yüzden her iki rol de zaten fiyat
        // görenler listesindedir (CanSeePricing). Firma kullanıcısı bu ekranlara
        // hiç giremez — ayrıca ProgressPayment'ta firma izolasyon filtresi de var.
        // ---------------------------------------------------------------
        options.AddPolicy(PolicyNames.CanViewProgressPayments, policy =>
            policy.RequireRole(RoleNames.Budget, RoleNames.BudgetManager));

        // Hakedişi Bütçe kurar ve yöneticiye gönderir; Bütçe Yöneticisi kurmaz.
        options.AddPolicy(PolicyNames.CanManageProgressPayment, policy =>
            policy.RequireRole(RoleNames.Budget));

        // Kararı yalnızca Bütçe Yöneticisi verir; Bütçe kendi hazırladığı
        // hakedişi onaylayamaz (mail yolunda da aynı kural — Bölüm B).
        options.AddPolicy(PolicyNames.CanApproveProgressPayment, policy =>
            policy.RequireRole(RoleNames.BudgetManager));

        options.AddPolicy(PolicyNames.CanManageUsers, policy =>
            policy.RequireAssertion(ctx =>
                ctx.User.IsInRole(RoleNames.Admin) ||
                (ctx.User.HasClaim(c => c.Type == AppClaimTypes.FirmId) &&
                 ctx.User.HasClaim(c => c.Type == AppClaimTypes.IsFirmAdmin && c.Value == "true"))));
    }
}
