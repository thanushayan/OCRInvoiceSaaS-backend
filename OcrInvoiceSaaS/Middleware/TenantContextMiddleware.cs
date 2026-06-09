namespace OcrInvoiceSaaS.Middleware;

/// <summary>
/// Reads the optional X-Company-Id header and stores it in HttpContext.Items
/// so services can resolve the active tenant without requiring it in every URL.
///
/// Usage in services:
///   var companyId = _httpContextAccessor.HttpContext?.GetActiveCompanyId();
/// </summary>
public class TenantContextMiddleware
{
    private const string HeaderName = "X-Company-Id";
    private const string ItemKey = "ActiveCompanyId";

    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var value)
            && Guid.TryParse(value, out var companyId))
        {
            context.Items[ItemKey] = companyId;
        }

        await _next(context);
    }
}

public static class HttpContextTenantExtensions
{
    private const string ItemKey = "ActiveCompanyId";

    /// <summary>Returns the company ID from X-Company-Id header, or null if not set.</summary>
    public static Guid? GetActiveCompanyId(this HttpContext context)
        => context.Items.TryGetValue(ItemKey, out var value) && value is Guid id ? id : null;
}
