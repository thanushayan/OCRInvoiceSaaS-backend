using OcrInvoiceSaaS.Interfaces;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Services;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

/// <summary>
/// Runs on the 1st of every month at 03:00 UTC.
/// Enforces per-company DataRetentionPolicy — hard-deletes records
/// that have exceeded their configured retention period.
///
/// AutoDeleteEnabled must be explicitly set to true per company (off by default).
/// Writes a SecurityEvent audit entry for every company processed.
/// </summary>
[DisallowConcurrentExecution]
public class DataRetentionJob : IJob
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<DataRetentionJob> _logger;

    public DataRetentionJob(ApplicationDbContext db, IAuditService audit, ILogger<DataRetentionJob> logger)
    {
        _db    = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("DataRetentionJob started at {Time}", DateTime.UtcNow);

        var policies = await _db.DataRetentionPolicies
            .Where(p => p.AutoDeleteEnabled)
            .ToListAsync();

        _logger.LogInformation("Processing {Count} companies with auto-delete enabled.", policies.Count);

        foreach (var policy in policies)
        {
            await ProcessCompanyAsync(policy);
        }

        _logger.LogInformation("DataRetentionJob completed.");
    }

    private async Task ProcessCompanyAsync(DataRetentionPolicy policy)
    {
        var summary = new List<string>();

        try
        {
            // 1. Invoices — delete records older than InvoiceRetentionYears
            var invoiceCutoff = DateTime.UtcNow.AddYears(-policy.InvoiceRetentionYears);
            var deletedInvoices = await _db.Invoices
                .Where(i =>
                    i.CompanyId == policy.CompanyId &&
                    i.Status == InvoiceStatus.Approved &&
                    i.CreatedAt < invoiceCutoff)
                .ExecuteDeleteAsync();

            if (deletedInvoices > 0)
                summary.Add($"{deletedInvoices} invoices (>{policy.InvoiceRetentionYears}yr)");

            // 2. Audit logs — delete older than AuditLogRetentionYears
            var auditCutoff = DateTime.UtcNow.AddYears(-policy.AuditLogRetentionYears);
            var deletedAudit = await _db.AuditLogs
                .Where(a => a.Timestamp < auditCutoff)
                .ExecuteDeleteAsync();

            if (deletedAudit > 0)
                summary.Add($"{deletedAudit} audit logs (>{policy.AuditLogRetentionYears}yr)");

            // 3. Login attempts — delete older than LoginAttemptRetentionDays
            var loginCutoff = DateTime.UtcNow.AddDays(-policy.LoginAttemptRetentionDays);
            var deletedLogins = await _db.LoginAttempts
                .Where(a => a.AttemptedAt < loginCutoff)
                .ExecuteDeleteAsync();

            if (deletedLogins > 0)
                summary.Add($"{deletedLogins} login attempts (>{policy.LoginAttemptRetentionDays}d)");

            // 4. Notifications — delete older than NotificationRetentionDays
            var notifCutoff = DateTime.UtcNow.AddDays(-policy.NotificationRetentionDays);
            var companyUserIds = await _db.CompanyUsers
                .Where(cu => cu.CompanyId == policy.CompanyId)
                .Select(cu => cu.UserId)
                .ToListAsync();

            var deletedNotifs = await _db.Notifications
                .Where(n => companyUserIds.Contains(n.UserId) && n.CreatedAt < notifCutoff)
                .ExecuteDeleteAsync();

            if (deletedNotifs > 0)
                summary.Add($"{deletedNotifs} notifications (>{policy.NotificationRetentionDays}d)");

            // 5. Idempotency records — always purge expired
            var deletedIdem = await _db.IdempotencyRecords
                .Where(r => r.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync();

            var summaryText = summary.Count > 0
                ? string.Join("; ", summary)
                : "Nothing to delete";

            _logger.LogInformation("DataRetention company {Id}: {Summary}", policy.CompanyId, summaryText);

            // Write compliance audit event
            await _audit.LogSecurityEventAsync(
                SecurityEventCategory.Compliance,
                SecurityEventSeverity.Info,
                "DATA_RETENTION_ENFORCED",
                $"Automatic data retention applied: {summaryText}",
                new SecurityEventContext
                {
                    CompanyId = policy.CompanyId,
                    Detail    = new { summary, processedAt = DateTime.UtcNow }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataRetentionJob failed for company {Id}", policy.CompanyId);
        }
    }
}
