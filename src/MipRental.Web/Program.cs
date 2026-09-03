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
builder.Services.AddScoped<RequestFlowService>();
builder.Services.AddScoped<RequestToWorkRecordService>();
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
        // Oturum AÇMIŞ ama yetkisi olmayan kullanıcıya giriş formu gösterilmemeli;
        // "zaten giriştesiniz" hissiyle kafa karıştırıyordu. Ayrı bir sayfa.
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization(AuthorizationPolicies.AddAppPolicies);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
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
    pattern: "{controller=Start}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
