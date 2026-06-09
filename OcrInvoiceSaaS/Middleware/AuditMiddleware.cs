using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Middleware;

/// <summary>
/// Automatically writes an AuditLog entry for every authenticated mutating request
/// (POST, PATCH, PUT, DELETE) and logs Security events for notable access patterns.
///
/// Read requests (GET) are not auto-logged to avoid log pollution —
/// they are logged explicitly where sensitive (e.g. bulk export, user profile access).
///
/// SOC 2 CC6.2 — Access to data by authorized users is logged.
/// ISO 27001 A.12.4.1 — Event logging.
/// </summary>
public class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditMiddleware> _logger;

    private static readonly HashSet<string> AuditedMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PATCH", "PUT", "DELETE" };

    private static readonly string[] SkippedPaths =
        ["/health", "/swagger", "/favicon", "/api/auth/refresh"];

    public AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        var method = context.Request.Method;
        var path   = context.Request.Path.Value ?? "";

        if (!AuditedMethods.Contains(method) ||
            !context.User.Identity?.IsAuthenticated == true ||
            SkippedPaths.Any(s => path.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            var auditService = context.RequestServices.GetRequiredService<IAuditService>();
            var ctx          = AuditContext.FromHttp(context);
            ctx.HttpStatusCode = context.Response.StatusCode;

            var userId = GetUserId(context);

            // Map HTTP path to entity type and action
            var (entityType, action) = ClassifyRequest(method, path, context.Response.StatusCode);

            var outcome = context.Response.StatusCode < 400 ? "Success"
                        : context.Response.StatusCode < 500 ? "Failure"
                        : "Error";

            await auditService.LogWithContextAsync(
                entityType, null, action, userId, ctx, outcome: outcome);

            // Fire security events for notable patterns
            await CheckForSecurityEvents(context, auditService, ctx, userId, path, method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AuditMiddleware failed to write log");
        }
    }

    private static async Task CheckForSecurityEvents(
        HttpContext context, IAuditService audit, AuditContext ctx,
        Guid? userId, string path, string method)
    {
        var status = context.Response.StatusCode;

        // 401 / 403 responses — failed access attempt
        if (status == 401 || status == 403)
        {
            await audit.LogSecurityEventAsync(
                SecurityEventCategory.Authorization,
                SecurityEventSeverity.Medium,
                status == 401 ? "AUTH_UNAUTHORIZED" : "AUTHZ_FORBIDDEN",
                $"{method} {path} returned {status}",
                new SecurityEventContext
                {
                    UserId    = userId,
                    IpAddress = ctx.IpAddress,
                    UserAgent = ctx.UserAgent,
                    RequestId = ctx.RequestId,
                    Detail    = new { method, path, status }
                });
        }

        // Bulk export or sensitive data access
        if (method == "GET" && (path.Contains("/export/") || path.Contains("/reports/")))
        {
            await audit.LogSecurityEventAsync(
                SecurityEventCategory.DataAccess,
                SecurityEventSeverity.Low,
                "DATA_EXPORT",
                $"Data export: {path}",
                new SecurityEventContext
                {
                    UserId    = userId,
                    IpAddress = ctx.IpAddress,
                    RequestId = ctx.RequestId,
                    Detail    = new { path }
                });
        }

        // GDPR erasure
        if (path.Contains("/erasure-request") && method == "POST" && status < 300)
        {
            await audit.LogSecurityEventAsync(
                SecurityEventCategory.Compliance,
                SecurityEventSeverity.High,
                "GDPR_ERASURE_REQUESTED",
                "GDPR right-to-erasure request submitted",
                new SecurityEventContext
                {
                    UserId         = userId,
                    IpAddress      = ctx.IpAddress,
                    RequestId      = ctx.RequestId,
                    RequiresReview = true
                });
        }

        // IP allowlist changed
        if (path.Contains("/ip-allowlist") && (method == "POST" || method == "DELETE") && status < 300)
        {
            await audit.LogSecurityEventAsync(
                SecurityEventCategory.Configuration,
                SecurityEventSeverity.Medium,
                "IP_ALLOWLIST_CHANGED",
                $"IP allowlist {(method == "POST" ? "entry added" : "entry removed")}",
                new SecurityEventContext
                {
                    UserId    = userId,
                    IpAddress = ctx.IpAddress,
                    RequestId = ctx.RequestId,
                    Detail    = new { method, path }
                });
        }

        // Invoice unlocked (immutability broken)
        if (path.Contains("/lock") && method == "DELETE" && status < 300)
        {
            await audit.LogSecurityEventAsync(
                SecurityEventCategory.DataMutation,
                SecurityEventSeverity.High,
                "INVOICE_UNLOCKED",
                "Approved invoice unlocked by owner — immutability override",
                new SecurityEventContext
                {
                    UserId         = userId,
                    IpAddress      = ctx.IpAddress,
                    RequestId      = ctx.RequestId,
                    RequiresReview = true,
                    Detail         = new { path }
                });
        }
    }

    private static (string entityType, string action) ClassifyRequest(
        string method, string path, int statusCode)
    {
        var segments = path.Trim('/').Split('/');
        var resource = segments.Length > 1 ? segments[^1] : segments[0];

        // Strip GUIDs from resource name
        if (Guid.TryParse(resource, out _) && segments.Length > 2)
            resource = segments[^2];

        var action = (method.ToUpper(), statusCode) switch
        {
            ("POST",   < 300) => "Created",
            ("PATCH",  < 300) => "Updated",
            ("PUT",    < 300) => "Updated",
            ("DELETE", < 300) => "Deleted",
            ("POST",   401)   => "CreateUnauthorized",
            ("POST",   403)   => "CreateForbidden",
            _                 => $"{method}_{statusCode}"
        };

        return (resource, action);
    }

    private static Guid? GetUserId(HttpContext context)
    {
        var claim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim?.Value, out var id) ? id : null;
    }
}
