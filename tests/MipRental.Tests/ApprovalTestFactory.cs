using Microsoft.AspNetCore.Http;
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

    internal sealed class NoOpTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
