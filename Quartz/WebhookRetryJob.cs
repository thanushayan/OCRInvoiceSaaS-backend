using OcrInvoiceSaaS.Interfaces;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

/// <summary>
/// Delivers queued webhook payloads and retries failed ones with
/// exponential backoff. Runs every 5 minutes.
/// </summary>
[DisallowConcurrentExecution]
public class WebhookRetryJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    public WebhookRetryJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookService>();
        await service.ProcessPendingDeliveriesAsync();
    }
}
