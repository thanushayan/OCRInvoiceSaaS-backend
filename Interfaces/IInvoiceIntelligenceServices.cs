using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Interfaces;

public interface IDuplicateDetectionService
{
    Task<DuplicateCheckResult> CheckAsync(Guid invoiceId, Guid companyId);
    Task<ServiceResult<List<DuplicateFlagResponse>>> GetFlagsForCompanyAsync(Guid companyId, Guid userId);
    Task<ServiceResult<List<DuplicateFlagResponse>>> GetFlagsForInvoiceAsync(Guid invoiceId, Guid userId);
    Task<ServiceResult> ReviewFlagAsync(Guid duplicateFlagId, ReviewDuplicateRequest request, Guid userId);
}

public interface IApprovalWorkflowService
{
    Task<ServiceResult<WorkflowTemplateResponse>> CreateTemplateAsync(Guid companyId, CreateWorkflowTemplateRequest request, Guid userId);
    Task<ServiceResult<List<WorkflowTemplateResponse>>> GetTemplatesAsync(Guid companyId, Guid userId);
    Task<ServiceResult> DeleteTemplateAsync(Guid templateId, Guid userId);

    Task<ServiceResult<ApprovalInstanceResponse>> StartApprovalAsync(Guid invoiceId, StartApprovalRequest request, Guid userId);
    Task<ServiceResult<ApprovalInstanceResponse>> GetInstanceAsync(Guid instanceId, Guid userId);
    Task<ServiceResult<List<ApprovalInstanceResponse>>> GetPendingForUserAsync(Guid userId);

    Task<ServiceResult<ApprovalInstanceResponse>> SubmitActionAsync(Guid instanceId, SubmitApprovalActionRequest request, Guid userId);
    Task<ServiceResult> CancelApprovalAsync(Guid instanceId, Guid userId);

    Task EscalateTimedOutStepsAsync();
}

public interface IBulkOcrService
{
    Task<ServiceResult<BulkOcrJobResponse>> CreateJobAsync(Guid companyId, CreateBulkOcrJobRequest request, Guid userId);
    Task<ServiceResult<BulkOcrJobResponse>> GetJobAsync(Guid jobId, Guid userId);
    Task<ServiceResult<List<BulkOcrJobResponse>>> GetJobsForCompanyAsync(Guid companyId, Guid userId);
    Task<ServiceResult> CancelJobAsync(Guid jobId, Guid userId);
    Task ProcessPendingJobsAsync();
}

public interface IPurchaseOrderService
{
    Task<ServiceResult<PurchaseOrderResponse>> CreateAsync(Guid companyId, CreatePurchaseOrderRequest request, Guid userId);
    Task<ServiceResult<List<PurchaseOrderResponse>>> GetByCompanyAsync(Guid companyId, Guid userId);
    Task<ServiceResult<PurchaseOrderResponse>> GetByIdAsync(Guid poId, Guid userId);
    Task<ServiceResult> DeleteAsync(Guid poId, Guid userId);
}

public interface IInvoiceMatchingService
{
    Task<ServiceResult<List<PoMatchResponse>>> AutoMatchAsync(Guid invoiceId, Guid userId);
    Task<ServiceResult<PoMatchResponse>> ManualMatchAsync(Guid invoiceId, MatchInvoiceToPoRequest request, Guid userId);
    Task<ServiceResult<List<PoMatchResponse>>> GetMatchesForInvoiceAsync(Guid invoiceId, Guid userId);
    Task<ServiceResult> DismissMatchAsync(Guid matchId, Guid userId);
}

public interface ICurrencyConversionService
{
    Task<ServiceResult<ExchangeRateResponse>> GetRateAsync(string fromCurrency, string toCurrency);
    Task<ServiceResult<CurrencyConversionResult>> ConvertAsync(string fromCurrency, string toCurrency, decimal amount);
    Task AttachRateToInvoiceAsync(Guid invoiceId, string invoiceCurrency, string baseCurrency);
    Task<ServiceResult<List<ExchangeRateResponse>>> GetCachedRatesAsync(string baseCurrency);
}
