using OcrInvoiceSaaS.Interfaces;
using System.Security.Claims;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Middleware;

/// <summary>
/// Reads the X-Api-Key header and, if valid, synthesises a ClaimsPrincipal
/// so the request flows through [Authorize] as if it were a JWT-authenticated call.
///
/// Also stores the active company ID and scopes in HttpContext.Items for
/// downstream scope enforcement.
///
/// Must be registered BEFORE UseAuthentication in Program.cs.
/// </summary>
public class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ScopesItemKey = "ApiKeyScopes";
    private const string CompanyItemKey = "ApiKeyCompanyId";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyValue))
        {
            var rawKey = apiKeyValue.ToString();

            // Resolve scoped service from DI
            var apiKeyService = context.RequestServices.GetRequiredService<IApiKeyService>();

            var (valid, userId, companyId, scopes) = await apiKeyService.ValidateAsync(rawKey);

            if (valid)
            {
                // Build a ClaimsPrincipal so [Authorize] passes
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, userId.ToString()),
                    new("auth_method", "apikey"),
                    new("api_key_company", companyId.ToString())
                };

                foreach (var scope in scopes)
                    claims.Add(new Claim("scope", scope));

                var identity = new ClaimsIdentity(claims, "ApiKey");
                context.User = new ClaimsPrincipal(identity);

                // Store for scope checking in controllers
                context.Items[ScopesItemKey] = scopes;
                context.Items[CompanyItemKey] = companyId;

                // Record IP for last-used tracking
                context.Items["ClientIp"] = GetClientIp(context);

                _logger.LogDebug("API key authenticated for user {UserId}, company {CompanyId}", userId, companyId);
            }
            else
            {
                _logger.LogWarning("Invalid API key presented from {Ip}", GetClientIp(context));
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired API key." });
                return;
            }
        }

        await _next(context);
    }

    private static string GetClientIp(HttpContext context)
        => context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
           ?? context.Connection.RemoteIpAddress?.ToString()
           ?? "unknown";
}

// ── Helper to enforce scopes in controllers ───────────────────────────────────

public static class ApiKeyScopeExtensions
{
    private const string ScopesItemKey = "ApiKeyScopes";

    /// <summary>
    /// Returns true if the request was authenticated via API key AND has the required scope.
    /// JWT-authenticated requests pass all scope checks automatically.
    /// </summary>
    public static bool HasScope(this HttpContext context, string requiredScope)
    {
        // JWT users bypass scope enforcement
        if (context.User.FindFirst("auth_method")?.Value != "apikey")
            return true;

        if (context.Items.TryGetValue(ScopesItemKey, out var scopesObj) &&
            scopesObj is string[] scopes)
        {
            return scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
        }

        return false;
    }
}
