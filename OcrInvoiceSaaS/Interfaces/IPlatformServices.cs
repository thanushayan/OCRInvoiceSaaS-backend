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

    /// <summary>Fire-and-forget event dispatch. Called internally after state changes.</summary>
    Task DispatchEventAsync(Guid companyId, WebhookEvent eventType, object payload);

    /// <summary>Called by Quartz job — retry failed/pending deliveries.</summary>
    Task ProcessPendingDeliveriesAsync();
}

public interface IActivityService
{
    Task<ServiceResult<List<ActivityResponse>>> GetForInvoiceAsync(Guid invoiceId, Guid userId);
    Task<ServiceResult<ActivityResponse>> AddCommentAsync(Guid invoiceId, AddCommentRequest request, Guid userId);
    Task<ServiceResult> DeleteCommentAsync(Guid activityId, Guid userId);

    /// <summary>Records a system-generated activity entry (status change, OCR complete, etc.).</summary>
    Task LogSystemEventAsync(Guid invoiceId, ActivityType type, string? metadata = null);
}

public interface IRateLimitService
{
    /// <summary>Check limit and increment counter. Returns false if throttled.</summary>
    Task<(bool allowed, RateLimitStatusResponse status)> CheckAndIncrementAsync(Guid companyId, string resource = "api");

    Task<ServiceResult<RateLimitStatusResponse>> GetStatusAsync(Guid companyId, Guid userId);
}
