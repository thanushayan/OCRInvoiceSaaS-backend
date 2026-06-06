using Microsoft.Extensions.DependencyInjection;
using OcrInvoiceSaaS.Interfaces;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

[DisallowConcurrentExecution]
public class ApprovalEscalationJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    public ApprovalEscalationJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IApprovalWorkflowService>();
        await service.EscalateOverdueStepsAsync();
    }
}

[DisallowConcurrentExecution]
public class OverdueTaskNotificationJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    public OverdueTaskNotificationJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITaskService>();
        await service.NotifyOverdueTasksAsync();
    }
}
