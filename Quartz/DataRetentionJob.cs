using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Services;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

[DisallowConcurrentExecution]
public class DataRetentionJob : IJob
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<DataRetentionJob> _logger;

    public DataRetentionJob(ApplicationDbContext db, IAuditService audit, ILogger<DataRetentionJob> logger)
    {
        _db = db; _audit = audit; _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("DataRetentionJob started at {Time}", DateTime.UtcNow);
        var policies = await _db.DataRetentionPolicies.Where(p => p.AutoDeleteEnabled).ToListAsync();
        foreach (var policy in policies) await ProcessCompanyAsync(policy);
        _logger.LogInformation("DataRetentionJob completed.");
    }

    private async Task ProcessCompanyAsync(DataRetentionPolicy policy)
    {
        try
        {
            var invoiceCutoff = DateTime.UtcNow.AddYears(-policy.InvoiceRetentionYears);
            await _db.Invoices.Where(i => i.CompanyId == policy.CompanyId && i.Status == InvoiceStatus.Approved && i.CreatedAt < invoiceCutoff).ExecuteDeleteAsync();

            var auditCutoff = DateTime.UtcNow.AddYears(-policy.AuditLogRetentionYears);
            await _db.AuditLogs.Where(a => a.Timestamp < auditCutoff).ExecuteDeleteAsync();

            var loginCutoff = DateTime.UtcNow.AddDays(-policy.LoginAttemptRetentionDays);
            await _db.LoginAttempts.Where(a => a.AttemptedAt < loginCutoff).ExecuteDeleteAsync();

            await _db.IdempotencyRecords.Where(r => r.ExpiresAt < DateTime.UtcNow).ExecuteDeleteAsync();

            _logger.LogInformation("DataRetention processed company {Id}", policy.CompanyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataRetentionJob failed for company {Id}", policy.CompanyId);
        }
    }
}
