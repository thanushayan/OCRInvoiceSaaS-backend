using System.Text.Json;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

// ══════════════════════════════════════════════════════════════════════════════
// Companies House API (UK company lookup by registration number)
// Docs: https://developer.company-information.service.gov.uk/
// ══════════════════════════════════════════════════════════════════════════════

public class CompaniesHouseService
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<CompaniesHouseService> _logger;

    public CompaniesHouseService(
        IHttpClientFactory http, IConfiguration config, ILogger<CompaniesHouseService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    public async Task<ServiceResult<CompaniesHouseLookupResponse>> LookupAsync(string companyNumber)
    {
        var clean = companyNumber.Trim().ToUpper().PadLeft(8, '0');

        var apiKey = _config["CompaniesHouse:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return ServiceResult<CompaniesHouseLookupResponse>.Fail(
                "Companies House API key not configured.", 503);

        try
        {
            var client = _http.CreateClient("CompaniesHouseClient");

            // CH uses HTTP Basic auth with API key as username, blank password
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{apiKey}:"));
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            var response = await client.GetAsync(
                $"https://api.company-information.service.gov.uk/company/{clean}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return ServiceResult<CompaniesHouseLookupResponse>.Fail(
                    $"Company {clean} not found in Companies House register.", 404);

            response.EnsureSuccessStatusCode();

            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;

            var address = BuildAddress(root);

            return ServiceResult<CompaniesHouseLookupResponse>.Success(new CompaniesHouseLookupResponse
            {
                CompanyNumber      = root.GetProperty("company_number").GetString() ?? clean,
                CompanyName        = root.GetProperty("company_name").GetString() ?? "",
                CompanyStatus      = root.TryGetProperty("company_status", out var cs) ? cs.GetString() : null,
                CompanyType        = root.TryGetProperty("type", out var ct) ? ct.GetString() : null,
                RegisteredAddress  = address,
                IncorporationDate  = root.TryGetProperty("date_of_creation", out var doc) ? doc.GetString() : null,
                Jurisdiction       = root.TryGetProperty("jurisdiction", out var jur) ? jur.GetString() : null,
                IsActive           = root.TryGetProperty("company_status", out var status) &&
                                     status.GetString() == "active",
                SicCodes           = root.TryGetProperty("sic_codes", out var sic)
                    ? string.Join(", ", sic.EnumerateArray().Select(s => s.GetString()))
                    : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Companies House lookup failed for {Number}", clean);
            return ServiceResult<CompaniesHouseLookupResponse>.Fail(
                "Companies House lookup failed. Please try again.", 502);
        }
    }

    private static string BuildAddress(JsonElement root)
    {
        if (!root.TryGetProperty("registered_office_address", out var addr)) return string.Empty;

        var parts = new[]
        {
            addr.TryGetProperty("address_line_1", out var l1) ? l1.GetString() : null,
            addr.TryGetProperty("address_line_2", out var l2) ? l2.GetString() : null,
            addr.TryGetProperty("locality", out var loc) ? loc.GetString() : null,
            addr.TryGetProperty("region", out var reg) ? reg.GetString() : null,
            addr.TryGetProperty("postal_code", out var pc) ? pc.GetString() : null,
            addr.TryGetProperty("country", out var co) ? co.GetString() : null
        };

        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// HMRC VAT Number Validation
// Uses EU VIES-compatible endpoint (post-Brexit UK uses its own check service)
// Docs: https://developer.service.hmrc.gov.uk/api-documentation/docs/api/service/vat-registered-companies-api/1.0
// ══════════════════════════════════════════════════════════════════════════════

public class HmrcVatService
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<HmrcVatService> _logger;

    public HmrcVatService(
        IHttpClientFactory http, IConfiguration config, ILogger<HmrcVatService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    public async Task<ServiceResult<VatValidationResponse>> ValidateAsync(string vatNumber)
    {
        // Normalise: strip spaces, ensure GB prefix
        var clean = vatNumber.Replace(" ", "").ToUpper();
        if (!clean.StartsWith("GB")) clean = "GB" + clean;

        // Strip GB prefix for the 9-digit part
        var numberOnly = clean[2..];

        if (numberOnly.Length != 9 || !numberOnly.All(char.IsDigit))
        {
            return ServiceResult<VatValidationResponse>.Success(new VatValidationResponse
            {
                VatNumber    = clean,
                IsValid      = false,
                CheckedAt    = DateTime.UtcNow,
                ErrorMessage = "Invalid UK VAT number format. Expected GB followed by 9 digits."
            });
        }

        var bearerToken = _config["Hmrc:BearerToken"];

        // If no HMRC credentials, fall back to format-only check
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            _logger.LogDebug("HMRC:BearerToken not configured — format check only for {Vat}", clean);
            return ServiceResult<VatValidationResponse>.Success(new VatValidationResponse
            {
                VatNumber    = clean,
                IsValid      = IsValidCheckDigit(numberOnly),
                CheckedAt    = DateTime.UtcNow,
                ErrorMessage = IsValidCheckDigit(numberOnly)
                    ? null
                    : "VAT number failed check digit validation."
            });
        }

        try
        {
            var client = _http.CreateClient("HmrcClient");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.hmrc.1.0+json");

            var response = await client.GetAsync(
                $"https://api.service.hmrc.gov.uk/organisations/vat/check-vat-number/lookup/{numberOnly}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ServiceResult<VatValidationResponse>.Success(new VatValidationResponse
                {
                    VatNumber    = clean,
                    IsValid      = false,
                    CheckedAt    = DateTime.UtcNow,
                    ErrorMessage = "VAT number not found in HMRC register."
                });
            }

            response.EnsureSuccessStatusCode();

            var json  = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root  = json.RootElement;
            var target = root.GetProperty("target");

            return ServiceResult<VatValidationResponse>.Success(new VatValidationResponse
            {
                VatNumber           = clean,
                IsValid             = true,
                BusinessName        = target.TryGetProperty("name", out var name) ? name.GetString() : null,
                BusinessAddress     = BuildVatAddress(target),
                ConsultationNumber  = root.TryGetProperty("consultationNumber", out var cn) ? cn.GetString() : null,
                CheckedAt           = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HMRC VAT check failed for {Vat}", clean);
            return ServiceResult<VatValidationResponse>.Fail(
                "HMRC VAT validation service unavailable. Format check passed.", 502);
        }
    }

    // UK VAT check digit algorithm (modulus 97)
    private static bool IsValidCheckDigit(string nineDigits)
    {
        if (nineDigits.Length != 9) return false;
        int[] weights = [8, 7, 6, 5, 4, 3, 2, 10, 1];
        var digits    = nineDigits.Select(c => c - '0').ToArray();

        int sum = digits.Zip(weights, (d, w) => d * w).Sum();
        return sum % 97 == 0;
    }

    private static string BuildVatAddress(JsonElement target)
    {
        if (!target.TryGetProperty("address", out var addr)) return string.Empty;

        var parts = new[]
        {
            addr.TryGetProperty("line1", out var l1) ? l1.GetString() : null,
            addr.TryGetProperty("line2", out var l2) ? l2.GetString() : null,
            addr.TryGetProperty("line3", out var l3) ? l3.GetString() : null,
            addr.TryGetProperty("line4", out var l4) ? l4.GetString() : null,
            addr.TryGetProperty("postCode", out var pc) ? pc.GetString() : null,
            addr.TryGetProperty("countryCode", out var cc) ? cc.GetString() : null,
        };

        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
