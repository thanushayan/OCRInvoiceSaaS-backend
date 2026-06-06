using System.Security.Claims;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Middleware;

/// <summary>
/// Per-company hourly rate limiter. Runs after authentication so we have a company context.
/// API key requests use the company embedded in the key; JWT requests use X-Company-Id header.
///
/// Returns 429 with Retry-After header when limit is exceeded.
/// Skips: health check, swagger, auth endpoints, static files.
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;

    private static readonly string[] SkippedPrefixes =
        ["/health", "/swagger", "/api/auth", "/favicon"];

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (SkippedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var companyId = ResolveCompanyId(context);
        if (companyId == null)
        {
            await _next(context);
            return;
        }

        var rateLimitService = context.RequestServices.GetRequiredService<IRateLimitService>();
        var (allowed, status) = await ((RateLimitService)rateLimitService)
            .CheckAndIncrementAsync(companyId.Value);

        // Always attach headers so clients can monitor usage
        context.Response.Headers["X-RateLimit-Limit"]     = status.RequestsLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = status.RequestsRemaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"]     = ((DateTimeOffset)status.WindowResetAt).ToUnixTimeSeconds().ToString();

        if (!allowed)
        {
            var retryAfter = (int)(status.WindowResetAt - DateTime.UtcNow).TotalSeconds;
            context.Response.Headers["Retry-After"] = retryAfter.ToString();
            context.Response.StatusCode = 429;

            await context.Response.WriteAsJsonAsync(new
            {
                error           = "Rate limit exceeded. Please slow down.",
                retryAfterSeconds = retryAfter,
                windowResetAt   = status.WindowResetAt
            });

            _logger.LogWarning("Rate limit exceeded for company {CompanyId} — {Used}/{Limit} requests this hour.",
                companyId, status.RequestsUsed, status.RequestsLimit);

            return;
        }

        await _next(context);
    }

    private static Guid? ResolveCompanyId(HttpContext context)
    {
        // API key path — company stored in items by ApiKeyMiddleware
        if (context.Items.TryGetValue("ApiKeyCompanyId", out var apiKeyCompany) &&
            apiKeyCompany is Guid apiKeyGuid)
            return apiKeyGuid;

        // JWT path — read X-Company-Id header
        if (context.Request.Headers.TryGetValue("X-Company-Id", out var headerValue) &&
            Guid.TryParse(headerValue, out var headerGuid))
            return headerGuid;

        return null;
    }
}
