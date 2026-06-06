using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Models;

namespace OcrInvoiceSaaS.Services;

public interface IAuditService
{
    Task LogAsync(string entityType, Guid? entityId, string action,
        Guid? performedByUserId, string? changeSummary = null, string outcome = "Success");

    Task LogWithContextAsync(string entityType, Guid? entityId, string action,
        Guid? performedByUserId, AuditContext ctx, string? changeSummary = null, string outcome = "Success");

    Task<List<AuditLogResponse>> GetLogsAsync(string entityType, Guid entityId, Guid requestingUserId);
    Task<List<AuditLogResponse>> GetByUserAsync(Guid targetUserId, Guid requestingUserId, int limit = 100);

    Task LogSecurityEventAsync(SecurityEventCategory category, SecurityEventSeverity severity,
        string eventCode, string description, SecurityEventContext ctx);

    Task<List<SecurityEventResponse>> GetSecurityEventsAsync(
        Guid companyId, Guid requestingUserId, SecurityEventFilter filter);

    Task MarkSecurityEventReviewedAsync(Guid eventId, Guid reviewedByUserId);
    Task<ChainVerificationResult> VerifyChainAsync(Guid companyId, DateTime from, DateTime to);
}

public class AuditContext
{
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? HttpMethod { get; set; }
    public string? HttpPath { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? RequestId { get; set; }
    public string? SessionId { get; set; }
    public string? AuthMethod { get; set; }
    public string? PerformedByEmail { get; set; }

    public static AuditContext FromHttp(HttpContext? http)
    {
        if (http == null) return new();
        return new AuditContext
        {
            IpAddress  = http.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                         ?? http.Connection.RemoteIpAddress?.ToString(),
            UserAgent  = http.Request.Headers.UserAgent.ToString(),
            HttpMethod = http.Request.Method,
            HttpPath   = http.Request.Path.Value,
            RequestId  = http.Request.Headers["X-Request-Id"].FirstOrDefault()
                         ?? http.TraceIdentifier,
            SessionId  = http.User.FindFirst("jti")?.Value,
            AuthMethod = http.User.FindFirst("auth_method")?.Value ?? "jwt",
            PerformedByEmail = http.User.FindFirst(
                System.Security.Claims.ClaimTypes.Email)?.Value
        };
    }
}

public class SecurityEventContext
{
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public Guid? CompanyId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestId { get; set; }
    public object? Detail { get; set; }
    public bool RequiresReview { get; set; } = false;
}

public class AuditLogResponse
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ChangeSummary { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? PerformedByEmail { get; set; }
    public string? PerformedByName { get; set; }
    public string? AuthMethod { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? HttpMethod { get; set; }
    public string? HttpPath { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? RequestId { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SecurityEventResponse
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string EventCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? UserEmail { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool RequiresReview { get; set; }
    public bool IsReviewed { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SecurityEventFilter
{
    public SecurityEventCategory? Category { get; set; }
    public SecurityEventSeverity? MinSeverity { get; set; }
    public bool? RequiresReview { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Limit { get; set; } = 100;
}

public class ChainVerificationResult
{
    public bool IsIntact { get; set; }
    public int EntriesVerified { get; set; }
    public int BrokenLinks { get; set; }
    public List<string> Issues { get; set; } = new();
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;
}

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public AuditService(ApplicationDbContext db, ILogger<AuditService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task LogAsync(string entityType, Guid? entityId, string action,
        Guid? performedByUserId, string? changeSummary = null, string outcome = "Success")
        => await LogWithContextAsync(entityType, entityId, action,
            performedByUserId, new AuditContext(), changeSummary, outcome);

    public async Task LogWithContextAsync(string entityType, Guid? entityId, string action,
        Guid? performedByUserId, AuditContext ctx, string? changeSummary = null, string outcome = "Success")
    {
        try
        {
            var email = ctx.PerformedByEmail;
            if (email == null && performedByUserId.HasValue)
                email = await _db.Users.Where(u => u.Id == performedByUserId)
                    .Select(u => u.Email).FirstOrDefaultAsync();

            var previousHash = await _db.AuditLogs
                .OrderByDescending(l => l.Timestamp)
                .Select(l => l.EntryHash)
                .FirstOrDefaultAsync();

            var entry = new AuditLog
            {
                EntityType        = entityType,
                EntityId          = entityId,
                Action            = action,
                ChangeSummary     = changeSummary,
                Outcome           = outcome,
                PerformedByUserId = performedByUserId,
                PerformedByEmail  = email,
                AuthMethod        = ctx.AuthMethod,
                IpAddress         = ctx.IpAddress,
                UserAgent         = ctx.UserAgent?[..Math.Min(500, ctx.UserAgent.Length)],
                HttpMethod        = ctx.HttpMethod,
                HttpPath          = ctx.HttpPath?[..Math.Min(500, ctx.HttpPath.Length)],
                HttpStatusCode    = ctx.HttpStatusCode,
                RequestId         = ctx.RequestId,
                SessionId         = ctx.SessionId,
                PreviousHash      = previousHash
            };

            entry.EntryHash = ComputeHash(entry);
            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit write failed: {EntityType}/{Action}", entityType, action);
        }
    }

    public async Task<List<AuditLogResponse>> GetLogsAsync(
        string entityType, Guid entityId, Guid requestingUserId)
    {
        return await _db.AuditLogs
            .Include(a => a.PerformedByUser)
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.Timestamp)
            .Select(a => Map(a)).ToListAsync();
    }

    public async Task<List<AuditLogResponse>> GetByUserAsync(
        Guid targetUserId, Guid requestingUserId, int limit = 100)
    {
        return await _db.AuditLogs
            .Include(a => a.PerformedByUser)
            .Where(a => a.PerformedByUserId == targetUserId)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => Map(a)).ToListAsync();
    }

    public async Task LogSecurityEventAsync(SecurityEventCategory category,
        SecurityEventSeverity severity, string eventCode, string description,
        SecurityEventContext ctx)
    {
        try
        {
            _db.SecurityEvents.Add(new SecurityEvent
            {
                Category       = category,
                Severity       = severity,
                EventCode      = eventCode,
                Description    = description,
                Detail         = ctx.Detail != null ? JsonSerializer.Serialize(ctx.Detail) : null,
                UserId         = ctx.UserId,
                UserEmail      = ctx.UserEmail,
                CompanyId      = ctx.CompanyId,
                IpAddress      = ctx.IpAddress,
                UserAgent      = ctx.UserAgent?[..Math.Min(500, ctx.UserAgent.Length)],
                RequestId      = ctx.RequestId,
                RequiresReview = ctx.RequiresReview || severity >= SecurityEventSeverity.High
            });

            await _db.SaveChangesAsync();

            if (severity >= SecurityEventSeverity.High)
                _logger.LogWarning("SECURITY [{Sev}] {Code}: {Desc} | {Email} @ {Ip}",
                    severity, eventCode, description, ctx.UserEmail, ctx.IpAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SecurityEvent write failed: {EventCode}", eventCode);
        }
    }

    public async Task<List<SecurityEventResponse>> GetSecurityEventsAsync(
        Guid companyId, Guid requestingUserId, SecurityEventFilter filter)
    {
        bool isAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == requestingUserId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isAdmin) return new();

        var q = _db.SecurityEvents
            .Where(e => e.CompanyId == companyId || e.CompanyId == null);

        if (filter.Category.HasValue)    q = q.Where(e => e.Category == filter.Category);
        if (filter.MinSeverity.HasValue) q = q.Where(e => e.Severity >= filter.MinSeverity);
        if (filter.RequiresReview.HasValue) q = q.Where(e => e.RequiresReview == filter.RequiresReview);
        if (filter.From.HasValue) q = q.Where(e => e.Timestamp >= filter.From);
        if (filter.To.HasValue)   q = q.Where(e => e.Timestamp <= filter.To);

        return await q.OrderByDescending(e => e.Timestamp).Take(filter.Limit)
            .Select(e => new SecurityEventResponse
            {
                Id             = e.Id,
                Category       = e.Category.ToString(),
                Severity       = e.Severity.ToString(),
                EventCode      = e.EventCode,
                Description    = e.Description,
                Detail         = e.Detail,
                UserEmail      = e.UserEmail,
                IpAddress      = e.IpAddress,
                UserAgent      = e.UserAgent,
                RequiresReview = e.RequiresReview,
                IsReviewed     = e.IsReviewed,
                Timestamp      = e.Timestamp
            }).ToListAsync();
    }

    public async Task MarkSecurityEventReviewedAsync(Guid eventId, Guid reviewedByUserId)
        => await _db.SecurityEvents.Where(e => e.Id == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsReviewed, true));

    public async Task<ChainVerificationResult> VerifyChainAsync(
        Guid companyId, DateTime from, DateTime to)
    {
        var logs = await _db.AuditLogs
            .Where(l => l.Timestamp >= from && l.Timestamp <= to)
            .OrderBy(l => l.Timestamp).ToListAsync();

        var result = new ChainVerificationResult { EntriesVerified = logs.Count };
        string? expectedPrev = logs.FirstOrDefault()?.PreviousHash;

        foreach (var log in logs)
        {
            if (log.EntryHash != ComputeHash(log))
            {
                result.BrokenLinks++;
                result.Issues.Add($"Hash mismatch on entry {log.Id} at {log.Timestamp:u}");
            }
            expectedPrev = log.EntryHash;
        }

        result.IsIntact = result.BrokenLinks == 0;
        return result;
    }

    private static string ComputeHash(AuditLog e)
    {
        var data  = $"{e.PreviousHash}|{e.Id}|{e.EntityType}|{e.Action}|{e.Timestamp:O}|{e.PerformedByUserId}|{e.Outcome}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static AuditLogResponse Map(AuditLog a) => new()
    {
        Id               = a.Id, EntityType = a.EntityType, EntityId = a.EntityId,
        Action           = a.Action, ChangeSummary = a.ChangeSummary, Outcome = a.Outcome,
        PerformedByEmail = a.PerformedByEmail, PerformedByName = a.PerformedByUser?.FullName,
        AuthMethod       = a.AuthMethod, IpAddress = a.IpAddress, UserAgent = a.UserAgent,
        HttpMethod       = a.HttpMethod, HttpPath = a.HttpPath, HttpStatusCode = a.HttpStatusCode,
        RequestId        = a.RequestId, Timestamp = a.Timestamp
    };
}
