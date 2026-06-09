using System.Text;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Middleware;

/// <summary>
/// Intercepts POST/PATCH requests with an Idempotency-Key header.
/// If the key + user combination was seen before and is still cached (24h),
/// replays the original response instead of re-processing the request.
///
/// Usage: clients add  Idempotency-Key: {uuid}  to any mutating request.
/// Safe methods (GET, DELETE) are never cached.
/// </summary>
public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly HashSet<string> CachedMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PATCH", "PUT" };

    private static readonly string[] SkippedPaths =
        ["/api/auth/login", "/api/auth/register", "/api/stripe", "/health", "/swagger"];

    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path   = context.Request.Path.Value ?? "";

        // Only intercept mutating methods on API routes
        if (!CachedMethods.Contains(method) ||
            !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            SkippedPaths.Any(s => path.StartsWith(s, StringComparison.OrdinalIgnoreCase)) ||
            !context.Request.Headers.TryGetValue("Idempotency-Key", out var keyValue))
        {
            await _next(context);
            return;
        }

        var key    = keyValue.ToString().Trim();
        var userId = GetUserId(context);

        if (string.IsNullOrWhiteSpace(key) || userId == Guid.Empty)
        {
            await _next(context);
            return;
        }

        var idempotency = context.RequestServices.GetRequiredService<IdempotencyService>();

        var (isDuplicate, cachedStatus, cachedBody) = await idempotency.CheckAsync(key, userId, path, method);

        if (isDuplicate && cachedBody != null)
        {
            // Replay cached response
            context.Response.StatusCode  = cachedStatus;
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Idempotency-Replayed"] = "true";
            await context.Response.WriteAsync(cachedBody);
            return;
        }

        // Capture the response body
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await _next(context);

        buffer.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(buffer).ReadToEndAsync();

        // Store for future duplicate requests
        if (context.Response.StatusCode is >= 200 and < 300)
        {
            await idempotency.StoreAsync(key, userId, path, method,
                context.Response.StatusCode, responseBody);
        }

        // Write buffered response to original stream
        buffer.Seek(0, SeekOrigin.Begin);
        context.Response.Body = originalBody;
        await buffer.CopyToAsync(originalBody);
    }

    private static Guid GetUserId(HttpContext context)
    {
        var claim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim?.Value, out var id) ? id : Guid.Empty;
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// IP Allowlist Enforcement Middleware
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Enforces per-company IP allowlists.
/// Reads the active company from X-Company-Id header and checks the client IP.
/// Skips enforcement when no allowlist rules exist for the company.
/// </summary>
public class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpAllowlistMiddleware> _logger;

    private static readonly string[] SkippedPaths =
        ["/health", "/swagger", "/api/auth/login", "/api/auth/register", "/api/stripe"];

    public IpAllowlistMiddleware(RequestDelegate next, ILogger<IpAllowlistMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (SkippedPaths.Any(s => path.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Resolve company from header or API key context
        Guid? companyId = null;

        if (context.Items.TryGetValue("ApiKeyCompanyId", out var apiKeyCompany) &&
            apiKeyCompany is Guid apiGuid)
            companyId = apiGuid;
        else if (context.Request.Headers.TryGetValue("X-Company-Id", out var hv) &&
                 Guid.TryParse(hv, out var hGuid))
            companyId = hGuid;

        if (companyId == null)
        {
            await _next(context);
            return;
        }

        var clientIp = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                       ?? context.Connection.RemoteIpAddress?.ToString()
                       ?? "unknown";

        var allowlistService = context.RequestServices.GetRequiredService<IpAllowlistService>();
        var allowed          = await allowlistService.IsAllowedAsync(companyId.Value, clientIp);

        if (!allowed)
        {
            _logger.LogWarning("Blocked request from {Ip} — not in allowlist for company {Id}",
                clientIp, companyId);

            context.Response.StatusCode  = 403;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Your IP address is not in the company allowlist. Contact your administrator."
            });
            return;
        }

        await _next(context);
    }
}
