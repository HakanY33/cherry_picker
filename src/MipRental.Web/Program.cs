using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using MipRental.Data;
using MipRental.Data.Approvals;
using MipRental.Data.Interceptors;
using MipRental.Data.Reporting;
using MipRental.Data.Pricing;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Domain.Entities;
using MipRental.Web.Documents;
using MipRental.Web.Security;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF Community lisansı: yıllık geliri 1M USD altındaki kuruluşlar için
// ücretsiz ve açıkça beyan edilmesi gerekiyor. Belge üretmeden önce kurulmalı.
QuestPDF.Settings.License = LicenseType.Community;

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    // CLAUDE.md: [Authorize] varsayılan olsun; [AllowAnonymous] sadece login'de.
    options.Filters.Add(new AuthorizeFilter());
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<LoginValidator>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSingleton<PeriodGuardInterceptor>();
builder.Services.AddSingleton<ImmutabilityGuardInterceptor>();
builder.Services.AddScoped<DocumentNumberService>();
builder.Services.AddScoped<ContractLineResolver>();
builder.Services.AddScoped<ApprovalFlowResolver>();
builder.Services.AddScoped<NotificationQueue>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<WorkRecordRevisionService>();
builder.Services.AddScoped<PeriodLockService>();
builder.Services.AddScoped<MonthlySummaryService>();
builder.Services.AddScoped<GeneratedDocumentService>();
builder.Services.AddScoped<DocumentVerificationService>();
builder.Services.AddScoped<DocumentGenerator>();

// Üretilen PDF'ler dosya sisteminde saklanır. Kök dizin yapılandırmadan okunur;
// verilmemişse içerik kökünün altındaki App_Data/documents kullanılır (uygulama
// wwwroot ALTINDA DEĞİL — belgeler statik dosya olarak servis edilmemeli).
builder.Services.AddSingleton<IDocumentStorage>(sp =>
{
    var configuredRoot = builder.Configuration["DocumentStorage:RootPath"];
    var root = string.IsNullOrWhiteSpace(configuredRoot)
        ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "documents")
        : configuredRoot;
    return new FileSystemDocumentStorage(root);
});

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.MigrationsAssembly("MipRental.Data"))
    .AddInterceptors(
        sp.GetRequiredService<AuditSaveChangesInterceptor>(),
        sp.GetRequiredService<PeriodGuardInterceptor>(),
        sp.GetRequiredService<ImmutabilityGuardInterceptor>()));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PolicyNames.MipStaff, policy =>
        policy.RequireAssertion(ctx => !ctx.User.HasClaim(c => c.Type == AppClaimTypes.FirmId)));

    options.AddPolicy(PolicyNames.FirmUser, policy =>
        policy.RequireClaim(AppClaimTypes.FirmId));

    options.AddPolicy(PolicyNames.CanApprove, policy =>
        policy.RequireRole(RoleNames.Supervisor, RoleNames.DeptHead));

    options.AddPolicy(PolicyNames.CanManageMaster, policy =>
        policy.RequireRole(RoleNames.Admin));

    options.AddPolicy(PolicyNames.CanManageContract, policy =>
        policy.RequireRole(RoleNames.Admin, RoleNames.Budget));

    options.AddPolicy(PolicyNames.CanClosePeriod, policy =>
        policy.RequireRole(RoleNames.Budget));

    options.AddPolicy(PolicyNames.CanManageUsers, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.IsInRole(RoleNames.Admin) ||
            (ctx.User.HasClaim(c => c.Type == AppClaimTypes.FirmId) &&
             ctx.User.HasClaim(c => c.Type == AppClaimTypes.IsFirmAdmin && c.Value == "true"))));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
