using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Interfaces;

public interface IReportingService
{
    Task<ServiceResult<SpendAnalyticsResponse>> GetSpendAnalyticsAsync(Guid companyId, ReportFilterRequest filter, Guid userId);
    Task<ServiceResult<VatSummaryResponse>> GetVatSummaryAsync(Guid companyId, DateTime periodStart, DateTime periodEnd, Guid userId);
    Task<ServiceResult<byte[]>> ExportInvoicesCsvAsync(Guid companyId, ReportFilterRequest filter, Guid userId);
    Task<ServiceResult<byte[]>> ExportInvoicesExcelAsync(Guid companyId, ReportFilterRequest filter, Guid userId);
}

public interface IWebhookService
{
    Task<ServiceResult<CreateWebhookResponse>> CreateEndpointAsync(Guid companyId, CreateWebhookRequest request, Guid userId);
    Task<ServiceResult<List<WebhookResponse>>> GetEndpointsAsync(Guid companyId, Guid userId);
    Task<ServiceResult> DeleteEndpointAsync(Guid endpointId, Guid userId);
    Task<ServiceResult<List<WebhookDeliveryResponse>>> GetDeliveriesAsync(Guid endpointId, Guid userId);
    Task<ServiceResult> RetryDeliveryAsync(Guid deliveryId, Guid userId);
    Task DispatchEventAsync(Guid companyId, WebhookEvent eventType, object payload);
    Task ProcessPendingDeliveriesAsync();
}

public interface IActivityService
{
    Task<ServiceResult<List<ActivityResponse>>> GetForInvoiceAsync(Guid invoiceId, Guid userId);
    Task<ServiceResult<ActivityResponse>> AddCommentAsync(Guid invoiceId, AddCommentRequest request, Guid userId);
    Task<ServiceResult> DeleteCommentAsync(Guid activityId, Guid userId);
    Task LogSystemEventAsync(Guid invoiceId, ActivityType type, string? metadata = null);
}

public interface IRateLimitService
{
    Task<(bool allowed, RateLimitStatusResponse status)> CheckAndIncrementAsync(Guid companyId, string resource = "api");
    Task<ServiceResult<RateLimitStatusResponse>> GetStatusAsync(Guid companyId, Guid userId);
}
