using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace OcrInvoiceSaaS.Libs;

public class CurrentUserProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetUserId()
    {
        var claim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)
                    ?? _httpContextAccessor.HttpContext?.User.FindFirst("sub");

        if (claim == null || !Guid.TryParse(claim.Value, out var userId))
            throw new UnauthorizedAccessException("User is not authenticated.");

        return userId;
    }

    public string GetEmail()
    {
        return _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value
               ?? string.Empty;
    }
}

public class JwtHelper
{
    private readonly IConfiguration _config;

    public JwtHelper(IConfiguration config)
    {
        _config = config;
    }

    public (string token, DateTime expiresAt) GenerateToken(Guid userId, string email, string fullName)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddHours(double.Parse(_config["Jwt:ExpiryHours"] ?? "24"));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, fullName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

// ── OCR Provider abstraction ──────────────────────────────────────────────────

public class OcrExtractedData
{
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? SubTotal { get; set; }
    public string? Currency { get; set; }
    public string? VendorName { get; set; }
    public string? VendorAddress { get; set; }
    public string? VendorVatNumber { get; set; }
    public string? RawText { get; set; }
}

public interface IOcrProvider
{
    string ProviderName { get; }
    Task<OcrExtractedData> ExtractAsync(string fileUrl, string fileType);
}

public class MockOcrProvider : IOcrProvider
{
    private static readonly string[] Companies =
    [
        "Acme Supplies Ltd", "BrightTech Solutions", "Northern Logistics PLC",
        "Green Energy Co", "FastPrint UK", "CloudBase Services Ltd"
    ];

    public string ProviderName => "MockProvider";

    public Task<OcrExtractedData> ExtractAsync(string fileUrl, string fileType)
    {
        var rng = new Random();
        var result = new OcrExtractedData
        {
            InvoiceNumber      = $"INV-{rng.Next(1000, 9999)}",
            InvoiceDate        = DateTime.UtcNow.AddDays(-rng.Next(1, 30)),
            DueDate            = DateTime.UtcNow.AddDays(rng.Next(14, 60)),
            TotalAmount        = Math.Round((decimal)(rng.NextDouble() * 5000 + 100), 2),
            TaxAmount          = Math.Round((decimal)(rng.NextDouble() * 500 + 10), 2),
            SubTotal           = Math.Round((decimal)(rng.NextDouble() * 4500 + 90), 2),
            Currency           = "GBP",
            VendorName         = Companies[rng.Next(Companies.Length)],
            VendorAddress      = $"{rng.Next(1, 200)} Business Park, London, UK",
            VendorVatNumber    = $"GB{rng.Next(100000000, 999999999)}",
            RawText            = $"Mock OCR extraction from {fileUrl}"
        };
        return Task.FromResult(result);
    }
}
