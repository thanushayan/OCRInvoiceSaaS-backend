using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Services;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

/// <summary>
/// Runs every 2 minutes to pick up queued bulk OCR jobs and process them in batches.
/// </summary>
[DisallowConcurrentExecution]
public class BulkOcrProcessingJob : IJob
{
    private readonly IBulkOcrService _bulkOcrService;
    private readonly ILogger<BulkOcrProcessingJob> _logger;

    public BulkOcrProcessingJob(IBulkOcrService bulkOcrService, ILogger<BulkOcrProcessingJob> logger)
    {
        _bulkOcrService = bulkOcrService;
        _logger         = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogDebug("BulkOcrProcessingJob triggered at {Time}", DateTime.UtcNow);
        await _bulkOcrService.ProcessPendingJobsAsync();
    }
}

/// <summary>
/// Runs every hour to check approval steps that have exceeded their timeout
/// and send escalation notifications (or auto-skip optional steps).
/// </summary>
[DisallowConcurrentExecution]
public class ApprovalEscalationJob : IJob
{
    private readonly IApprovalWorkflowService _approvalService;
    private readonly ILogger<ApprovalEscalationJob> _logger;

    public ApprovalEscalationJob(IApprovalWorkflowService approvalService, ILogger<ApprovalEscalationJob> logger)
    {
        _approvalService = approvalService;
        _logger          = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogDebug("ApprovalEscalationJob triggered at {Time}", DateTime.UtcNow);
        await _approvalService.EscalateTimedOutStepsAsync();
    }
}
