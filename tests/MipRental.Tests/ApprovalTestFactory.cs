using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using MipRental.Data;
using MipRental.Data.Approvals;
using MipRental.Data.Services;
using MipRental.Domain.Abstractions;
using MipRental.Web.Controllers;
using MipRental.Web.Documents;

namespace MipRental.Tests;

// Onay akışı testlerinde tekrar eden kurulum. Servisler DI olmadan elle
// bağlanır; üretimdeki bağımlılık grafiğiyle aynı olması için tek yerde.
internal static class ApprovalTestFactory
{
    public static ApprovalService CreateApprovalService(AppDbContext db, ICurrentUser currentUser) =>
        new(db, currentUser, new ApprovalFlowResolver(db), new NotificationQueue(db));

    public static ApprovalsController CreateApprovalsController(AppDbContext db, ICurrentUser currentUser) =>
        new(db, CreateApprovalService(db, currentUser), currentUser)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
        };

    public static DocumentGenerator CreateDocumentGenerator(
        AppDbContext db, ICurrentUser currentUser, IDocumentStorage? storage = null) =>
        new(db, new GeneratedDocumentService(db, currentUser, storage ?? new InMemoryDocumentStorage()));

    public static WorkRecordsController CreateWorkRecordsController(AppDbContext db, ICurrentUser currentUser) =>
        new(db, currentUser, new MipRental.Data.Pricing.ContractLineResolver(db), new DocumentNumberService(db),
            CreateApprovalService(db, currentUser), new WorkRecordRevisionService(db, currentUser),
            CreateDocumentGenerator(db, currentUser))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
        };

    // Adım 11 — talep ekranları. Aynı desen: DI olmadan, üretimdeki bağımlılık
    // grafiğiyle aynı şekilde elle bağlanır.
    public static RequestFlowService CreateRequestFlowService(AppDbContext db, ICurrentUser currentUser) =>
        new(db, CreateApprovalService(db, currentUser));

    public static RequestsController CreateRequestsController(AppDbContext db, ICurrentUser currentUser) =>
        new(db, currentUser, CreateRequestFlowService(db, currentUser), new DocumentNumberService(db), new NotificationQueue(db))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
        };

    public static EquipmentRequestsController CreateEquipmentRequestsController(
        AppDbContext db, ICurrentUser currentUser, IAuthorizationService authorization, ClaimsPrincipal principal) =>
        new(db, CreateRequestFlowService(db, currentUser), new NotificationQueue(db), authorization)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
        };

    // Adım 12 — türetme servisi ve operatör ekranı.
    public static RequestToWorkRecordService CreateDerivationService(AppDbContext db, ICurrentUser currentUser) =>
        new(db, new MipRental.Data.Pricing.ContractLineResolver(db), currentUser, new NotificationQueue(db));

    public static FirmOperatorController CreateFirmOperatorController(AppDbContext db, ICurrentUser currentUser) =>
        new(db, CreateRequestFlowService(db, currentUser), CreateDerivationService(db, currentUser), new NotificationQueue(db))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
        };

    public static FirmRequestsController CreateFirmRequestsController(AppDbContext db, ICurrentUser currentUser) =>
        new(db, CreateRequestFlowService(db, currentUser), new NotificationQueue(db))
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NoOpTempDataProvider())
        };

    internal sealed class NoOpTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
