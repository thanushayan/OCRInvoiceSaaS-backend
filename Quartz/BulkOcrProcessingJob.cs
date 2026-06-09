using OcrInvoiceSaaS.Interfaces;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

/// <summary>
/// Drains queued bulk OCR jobs in small batches every 2 minutes
/// so a 100-invoice job never blocks a worker for its full duration.
/// </summary>
[DisallowConcurrentExecution]
public class BulkOcrProcessingJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    public BulkOcrProcessingJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBulkOcrService>();
        await service.ProcessPendingJobsAsync();
    }
}
