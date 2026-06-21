using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
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
            InvoiceNumber   = $"INV-{rng.Next(1000, 9999)}",
            InvoiceDate     = DateTime.UtcNow.AddDays(-rng.Next(1, 30)),
            DueDate         = DateTime.UtcNow.AddDays(rng.Next(14, 60)),
            TotalAmount     = Math.Round((decimal)(rng.NextDouble() * 5000 + 100), 2),
            TaxAmount       = Math.Round((decimal)(rng.NextDouble() * 500 + 10), 2),
            SubTotal        = Math.Round((decimal)(rng.NextDouble() * 4500 + 90), 2),
            Currency        = "GBP",
            VendorName      = Companies[rng.Next(Companies.Length)],
            VendorAddress   = $"{rng.Next(1, 200)} Business Park, London, UK",
            VendorVatNumber = $"GB{rng.Next(100000000, 999999999)}",
            RawText         = $"Mock OCR extraction from {fileUrl}"
        };
        return Task.FromResult(result);
    }
}

public class GcpVisionProvider : IOcrProvider
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GcpVisionProvider> _logger;

    public string ProviderName => "GoogleCloudVision";

    public GcpVisionProvider(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<GcpVisionProvider> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<OcrExtractedData> ExtractAsync(string fileUrl, string fileType)
    {
        var apiKey = _config["Gcp:VisionApiKey"]
            ?? throw new InvalidOperationException("GCP Vision API key not configured.");

        byte[] fileBytes;
        if (fileUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var http = _httpClientFactory.CreateClient();
            fileBytes = await http.GetByteArrayAsync(fileUrl);
        }
        else
        {
            fileBytes = await File.ReadAllBytesAsync(fileUrl);
        }

        var base64Image = Convert.ToBase64String(fileBytes);
        var requestBody = new
        {
            requests = new[]
            {
                new
                {
                    image    = new { content = base64Image },
                    features = new[] { new { type = "DOCUMENT_TEXT_DETECTION" } }
                }
            }
        };

        var client  = _httpClientFactory.CreateClient("GcpVisionClient");
        var json    = System.Text.Json.JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(
            $"https://vision.googleapis.com/v1/images:annotate?key={apiKey}", content);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc      = System.Text.Json.JsonDocument.Parse(responseJson);
        var fullText = doc.RootElement
            .GetProperty("responses")[0]
            .GetProperty("fullTextAnnotation")
            .GetProperty("text")
            .GetString() ?? string.Empty;

        _logger.LogInformation("GCP Vision extracted {Chars} characters", fullText.Length);
        return ParseExtractedText(fullText);
    }

    private static OcrExtractedData ParseExtractedText(string text)
    {
        var lines  = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var result = new OcrExtractedData { RawText = text };

        // ── Currency detection from full text ──
        var currencyMap = new Dictionary<string, string>
        {
            { "£", "GBP" }, { "$", "USD" }, { "€", "EUR" }, { "₹", "INR" }
        };
        foreach (var kv in currencyMap)
            if (text.Contains(kv.Key)) { result.Currency = kv.Value; break; }

        // ── VAT / Tax number (GB123456789, IE1234567X) ──
        var vatMatch = Regex.Match(text, @"\b(GB\d{9}|\d{9}B|IE\w{8,9})\b",
            RegexOptions.IgnoreCase);
        if (vatMatch.Success)
            result.VendorVatNumber = vatMatch.Value.ToUpper();

        foreach (var line in lines)
        {
            var lower   = line.ToLower().Trim();
            var trimmed = line.Trim();

            // ── Invoice Number ──
            if (result.InvoiceNumber == null &&
                (lower.Contains("invoice no")     || lower.Contains("invoice #")   ||
                 lower.Contains("invoice number") || lower.Contains("inv no")      ||
                 lower.Contains("inv#")           || lower.Contains("invoice ref") ||
                 lower.Contains("reference no")   || lower.Contains("ref no")      ||
                 lower.Contains("number:")))
            {
                var val = ExtractAfterColon(trimmed) ?? ExtractAfterLastSpace(trimmed);
                if (!string.IsNullOrWhiteSpace(val))
                    result.InvoiceNumber = val;
            }

            // ── Invoice Date ──
            if (result.InvoiceDate == null &&
                (lower.Contains("invoice date") || lower.Contains("date of invoice") ||
                 lower.Contains("issue date")   || lower.Contains("date issued")     ||
                 lower.Contains("billing date") || lower == "date"))
            {
                var val = ExtractAfterColon(trimmed);
                if (val != null && DateTime.TryParse(val, out var d))
                    result.InvoiceDate = d;
            }

            // ── Due Date ──
            if (result.DueDate == null &&
                (lower.Contains("due date")    || lower.Contains("payment due")  ||
                 lower.Contains("pay by")      || lower.Contains("due by")       ||
                 lower.Contains("payment date")))
            {
                var val = ExtractAfterColon(trimmed);
                if (val != null && DateTime.TryParse(val, out var d))
                    result.DueDate = d;
            }

            // ── Total Amount ──
            if (result.TotalAmount == null &&
                (lower.Contains("total amount") || lower.Contains("amount due")  ||
                 lower.Contains("grand total")  || lower.Contains("total due")   ||
                 lower.Contains("balance due")  || lower == "total"))
            {
                var val = ExtractDecimal(trimmed);
                if (val.HasValue) result.TotalAmount = val;
            }

            // ── Tax / VAT Amount ──
            if (result.TaxAmount == null &&
                (lower.Contains("vat")        || lower.Contains("tax amount") ||
                 lower.Contains("gst")        || lower.Contains("tax total")  ||
                 lower.Contains("sales tax")))
            {
                var val = ExtractDecimal(trimmed);
                if (val.HasValue) result.TaxAmount = val;
            }

            // ── SubTotal ──
            if (result.SubTotal == null &&
                (lower.Contains("subtotal")   || lower.Contains("sub total")  ||
                 lower.Contains("net amount") || lower.Contains("net total")))
            {
                var val = ExtractDecimal(trimmed);
                if (val.HasValue) result.SubTotal = val;
            }

            // ── Vendor Name — first meaningful non-header line ──
            if (result.VendorName == null && trimmed.Length > 3 &&
                !lower.StartsWith("invoice") && !lower.StartsWith("date")  &&
                !lower.StartsWith("bill")    && !lower.StartsWith("to:")   &&
                !lower.StartsWith("from:")   && !lower.StartsWith("page")  &&
                !lower.StartsWith("ref")     && !lower.StartsWith("vat")   &&
                !lower.StartsWith("tax")     && !lower.StartsWith("total") &&
                !lower.StartsWith("due"))
            {
                result.VendorName = trimmed;
            }
        }

        result.Currency ??= "GBP";
        return result;
    }

    private static string? ExtractAfterColon(string line)
    {
        var idx = line.IndexOf(':');
        return idx >= 0 ? line[(idx + 1)..].Trim() : null;
    }

    private static string? ExtractAfterLastSpace(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[^1] : null;
    }

    private static decimal? ExtractDecimal(string line)
    {
        var match = Regex.Match(line, @"[£$€₹]?\s*[\d,]+\.?\d*");
        if (!match.Success) return null;
        var cleaned = Regex.Replace(match.Value, @"[£$€₹\s,]", "");
        return decimal.TryParse(cleaned,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
