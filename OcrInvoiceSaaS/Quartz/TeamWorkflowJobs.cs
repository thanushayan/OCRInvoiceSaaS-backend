using OcrInvoiceSaaS.Interfaces;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

/// <summary>
/// Runs daily at 09:00 UTC — sends notifications for overdue open/in-progress tasks.
/// </summary>
[DisallowConcurrentExecution]
public class OverdueTaskNotificationJob : IJob
{
    private readonly ITaskService _taskService;
    private readonly ILogger<OverdueTaskNotificationJob> _logger;

    public OverdueTaskNotificationJob(ITaskService taskService, ILogger<OverdueTaskNotificationJob> logger)
    {
        _taskService = taskService;
        _logger      = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("OverdueTaskNotificationJob triggered at {Time}", DateTime.UtcNow);
        await _taskService.NotifyOverdueTasksAsync();
    }
}
