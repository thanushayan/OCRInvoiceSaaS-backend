using System.Security.Claims;

namespace OcrInvoiceSaaS.Libs;

/// <summary>
/// Resolves the authenticated user from the current HTTP context.
/// Works for both JWT bearer tokens and API key requests
/// (ApiKeyMiddleware sets the same NameIdentifier claim).
/// </summary>
public class CurrentUserProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserProvider(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public Guid GetUserId()
    {
        var claim = _httpContextAccessor.HttpContext?.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(claim, out var id)
            ? id
            : throw new UnauthorizedAccessException("User identity could not be resolved from the request.");
    }

    public string? GetEmail()
        => _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value;
}
