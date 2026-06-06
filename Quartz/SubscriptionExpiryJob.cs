using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

[DisallowConcurrentExecution]
public class SubscriptionExpiryJob : IJob
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SubscriptionExpiryJob> _logger;

    public SubscriptionExpiryJob(ApplicationDbContext db, ILogger<SubscriptionExpiryJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("SubscriptionExpiryJob started at {Time}", DateTime.UtcNow);

        var expired = await _db.CompanySubscriptions
            .Where(cs => cs.IsActive &&
                         cs.EndDate.HasValue &&
                         cs.EndDate.Value < DateTime.UtcNow)
            .ToListAsync();

        _logger.LogInformation("Found {Count} subscriptions to expire.", expired.Count);

        foreach (var sub in expired)
        {
            sub.IsActive = false;
            sub.Status = "Expired";
            sub.UpdatedAt = DateTime.UtcNow;
        }

        if (expired.Count > 0)
            await _db.SaveChangesAsync();

        _logger.LogInformation("SubscriptionExpiryJob finished.");
    }
}
