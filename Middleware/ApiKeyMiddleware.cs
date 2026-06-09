using System.Security.Claims;
using OcrInvoiceSaaS.Interfaces;

namespace OcrInvoiceSaaS.Middleware;

/// <summary>
/// Authenticates machine-to-machine requests via the X-Api-Key header.
/// Runs before UseAuthentication: when a valid key is presented it sets
/// HttpContext.User with the same NameIdentifier claim a JWT would carry,
/// so [Authorize] and CurrentUserProvider work unchanged.
///
/// Also stores the key's company and scopes in HttpContext.Items for
/// RateLimitMiddleware ("ApiKeyCompanyId") and scope checks downstream.
/// </summary>
public class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var headerValue))
        {
            await _next(context);
            return;
        }

        var rawKey = headerValue.ToString().Trim();
        var apiKeyService = context.RequestServices.GetRequiredService<IApiKeyService>();

        var (valid, userId, companyId, scopes) = await apiKeyService.ValidateAsync(rawKey);

        if (!valid)
        {
            _logger.LogWarning("Rejected request with invalid API key (prefix: {Prefix})",
                rawKey.Length >= 8 ? rawKey[..8] : "?");

            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired API key." });
            return;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("auth_method", "apikey"),
            new("company_id", companyId.ToString())
        };
        claims.AddRange(scopes.Select(s => new Claim("scope", s)));

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "ApiKey"));

        context.Items["ApiKeyCompanyId"] = companyId;
        context.Items["ApiKeyUserId"] = userId;
        context.Items["ApiKeyScopes"] = scopes;

        await _next(context);
    }
}
