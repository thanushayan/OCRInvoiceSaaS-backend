using OcrInvoiceSaaS.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Controllers;

/// <summary>
/// Compliance management — data retention policies, extended audit trail,
/// security event log, and chain-of-custody verification.
/// All endpoints require Owner or Admin role.
/// </summary>
[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/compliance")]
public class ComplianceController : ControllerBase
{
    private readonly IAuditService _audit;
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserProvider _currentUser;

    public ComplianceController(IAuditService audit, ApplicationDbContext db, CurrentUserProvider currentUser)
    {
        _audit       = audit;
        _db          = db;
        _currentUser = currentUser;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Data Retention Policy
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the data retention policy for this company.
    /// Defaults: 7 years invoices, 7 years audit logs, 90 days login attempts.
    /// AutoDeleteEnabled is off by default — enable explicitly.
    /// </summary>
    [HttpGet("retention")]
    public async Task<IActionResult> GetRetentionPolicy(Guid companyId)
    {
        if (!await IsAdminAsync(companyId)) return Forbid();

        var policy = await _db.DataRetentionPolicies.FirstOrDefaultAsync(p => p.CompanyId == companyId);

        if (policy == null)
        {
            return Ok(new DataRetentionPolicyRequest
            {
                InvoiceRetentionYears       = 7,
                AuditLogRetentionYears      = 7,
                LoginAttemptRetentionDays   = 90,
                NotificationRetentionDays   = 90,
                AutoDeleteEnabled           = false
            });
        }

        return Ok(new DataRetentionPolicyRequest
        {
            InvoiceRetentionYears     = policy.InvoiceRetentionYears,
            AuditLogRetentionYears    = policy.AuditLogRetentionYears,
            LoginAttemptRetentionDays = policy.LoginAttemptRetentionDays,
            NotificationRetentionDays = policy.NotificationRetentionDays,
            AutoDeleteEnabled         = policy.AutoDeleteEnabled
        });
    }

    /// <summary>
    /// Update the data retention policy.
    /// UK legal minimum for financial records is 7 years (HMRC requirement).
    /// Setting AutoDeleteEnabled=true will activate monthly auto-deletion.
    /// </summary>
    [HttpPut("retention")]
    public async Task<IActionResult> UpdateRetentionPolicy(
        Guid companyId, [FromBody] DataRetentionPolicyRequest request)
    {
        if (!await IsOwnerAsync(companyId))
            return StatusCode(403, new { error = "Only company owners can change data retention policies." });

        var policy = await _db.DataRetentionPolicies.FirstOrDefaultAsync(p => p.CompanyId == companyId);

        if (policy == null)
        {
            policy = new DataRetentionPolicy { CompanyId = companyId };
            _db.DataRetentionPolicies.Add(policy);
        }

        policy.InvoiceRetentionYears       = request.InvoiceRetentionYears;
        policy.AuditLogRetentionYears      = request.AuditLogRetentionYears;
        policy.LoginAttemptRetentionDays   = request.LoginAttemptRetentionDays;
        policy.NotificationRetentionDays   = request.NotificationRetentionDays;
        policy.AutoDeleteEnabled           = request.AutoDeleteEnabled;
        policy.UpdatedAt                   = DateTime.UtcNow;
        policy.UpdatedByUserId             = _currentUser.GetUserId();

        await _db.SaveChangesAsync();

        await _audit.LogSecurityEventAsync(
            SecurityEventCategory.Configuration,
            SecurityEventSeverity.Medium,
            "RETENTION_POLICY_UPDATED",
            $"Data retention policy updated. AutoDelete: {request.AutoDeleteEnabled}",
            new SecurityEventContext
            {
                UserId    = _currentUser.GetUserId(),
                CompanyId = companyId,
                Detail    = request
            });

        return Ok(new { message = "Data retention policy updated.", autoDeleteEnabled = request.AutoDeleteEnabled });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Extended Audit Trail
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the extended audit trail for any entity — includes IP address,
    /// user agent, HTTP method/path, auth method, and outcome.
    /// </summary>
    [HttpGet("audit/{entityType}/{entityId:guid}")]
    public async Task<IActionResult> GetAuditTrail(Guid companyId, string entityType, Guid entityId)
    {
        if (!await IsAdminAsync(companyId)) return Forbid();

        var logs = await _audit.GetLogsAsync(entityType, entityId, _currentUser.GetUserId());
        return Ok(logs);
    }

    /// <summary>
    /// Get the complete audit history for a specific user within this company.
    /// Useful for user access reviews (SOC 2 CC6.3).
    /// </summary>
    [HttpGet("audit/user/{targetUserId:guid}")]
    public async Task<IActionResult> GetUserAuditTrail(
        Guid companyId, Guid targetUserId, [FromQuery] int limit = 100)
    {
        if (!await IsAdminAsync(companyId)) return Forbid();

        var logs = await _audit.GetByUserAsync(targetUserId, _currentUser.GetUserId(), limit);
        return Ok(logs);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Security Event Log (SOC 2 CC7 / ISO 27001 A.12.4.1)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Query the security event log.
    /// Supports filtering by category, severity, date range, and review status.
    /// High/Critical events are automatically flagged for review.
    ///
    /// Event categories: Authentication, Authorization, DataAccess, DataMutation,
    ///                   Configuration, Compliance, Integration, AnomalyDetected
    /// Severity levels: Info, Low, Medium, High, Critical
    /// </summary>
    [HttpGet("security-events")]
    public async Task<IActionResult> GetSecurityEvents(
        Guid companyId,
        [FromQuery] SecurityEventCategory? category,
        [FromQuery] SecurityEventSeverity? minSeverity,
        [FromQuery] bool? requiresReview,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 100)
    {
        if (!await IsAdminAsync(companyId)) return Forbid();

        var events = await _audit.GetSecurityEventsAsync(companyId, _currentUser.GetUserId(),
            new SecurityEventFilter
            {
                Category       = category,
                MinSeverity    = minSeverity,
                RequiresReview = requiresReview,
                From           = from,
                To             = to,
                Limit          = Math.Min(limit, 500)
            });

        return Ok(events);
    }

    /// <summary>
    /// Mark a security event as reviewed (acknowledge it has been investigated).
    /// Required for SOC 2 CC7.2 compliance — all High/Critical events must be reviewed.
    /// </summary>
    [HttpPost("security-events/{eventId:guid}/review")]
    public async Task<IActionResult> ReviewSecurityEvent(Guid companyId, Guid eventId)
    {
        if (!await IsAdminAsync(companyId)) return Forbid();

        await _audit.MarkSecurityEventReviewedAsync(eventId, _currentUser.GetUserId());
        return Ok(new { message = "Security event marked as reviewed." });
    }

    /// <summary>
    /// Get a summary count of unreviewed High/Critical security events.
    /// Use for dashboard alert badges.
    /// </summary>
    [HttpGet("security-events/pending-review/count")]
    public async Task<IActionResult> GetPendingReviewCount(Guid companyId)
    {
        if (!await IsAdminAsync(companyId)) return Forbid();

        var count = await _db.SecurityEvents
            .CountAsync(e =>
                (e.CompanyId == companyId || e.CompanyId == null) &&
                e.RequiresReview &&
                !e.IsReviewed);

        return Ok(new { count });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Audit Chain Verification (tamper detection)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verify the tamper-evident hash chain for audit logs in a date range.
    /// Each audit log entry contains a SHA-256 hash of itself chained to the previous entry.
    /// Returns details of any broken links indicating potential tampering.
    /// ISO 27001 A.12.4.2 — Protection of log information.
    /// </summary>
    [HttpGet("audit/verify-chain")]
    public async Task<IActionResult> VerifyAuditChain(
        Guid companyId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        if (!await IsOwnerAsync(companyId))
            return StatusCode(403, new { error = "Only owners can run chain verification." });

        var result = await _audit.VerifyChainAsync(companyId, from, to);
        return Ok(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> IsAdminAsync(Guid companyId)
        => await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == _currentUser.GetUserId() &&
            (cu.Role == "Owner" || cu.Role == "Admin"));

    private async Task<bool> IsOwnerAsync(Guid companyId)
        => await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == _currentUser.GetUserId() &&
            cu.Role == "Owner");
}
