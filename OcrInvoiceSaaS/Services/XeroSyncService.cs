using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

/// <summary>
/// Xero OAuth 2.0 integration.
/// Pushes approved invoices to Xero as Bills (accounts payable).
/// Docs: https://developer.xero.com/documentation/api/accounting/invoices
/// </summary>
public class XeroSyncService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<XeroSyncService> _logger;

    private string ClientId     => _config["Xero:ClientId"] ?? "";
    private string ClientSecret => _config["Xero:ClientSecret"] ?? "";
    private string RedirectUri  => _config["Xero:RedirectUri"] ?? "";

    public XeroSyncService(
        ApplicationDbContext db, IHttpClientFactory http,
        IConfiguration config, ILogger<XeroSyncService> logger)
    {
        _db     = db;
        _http   = http;
        _config = config;
        _logger = logger;
    }

    // ── OAuth ─────────────────────────────────────────────────────────────────

    public string BuildAuthUrl(Guid companyId)
    {
        var state = Convert.ToBase64String(Encoding.UTF8.GetBytes(companyId.ToString()));
        return $"https://login.xero.com/identity/connect/authorize" +
               $"?response_type=code&client_id={ClientId}" +
               $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
               $"&scope=offline_access+accounting.transactions+accounting.contacts" +
               $"&state={state}";
    }

    public async Task<ServiceResult<AccountingConnectionResponse>> HandleCallbackAsync(
        Guid companyId, string code, Guid userId)
    {
        bool isAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == userId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isAdmin) return ServiceResult<AccountingConnectionResponse>.Fail("Only owners/admins can connect integrations.", 403);

        try
        {
            // Exchange code for tokens
            var tokenResponse = await ExchangeCodeAsync(code);

            // Get connected tenants
            var tenants = await GetTenantsAsync(tokenResponse.AccessToken);
            if (!tenants.Any()) throw new Exception("No Xero organisation found.");
            var tenant = tenants.First();

            // Deactivate any existing connection
            await _db.AccountingConnections
                .Where(c => c.CompanyId == companyId && c.Provider == AccountingProvider.Xero)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));

            var connection = new AccountingConnection
            {
                CompanyId              = companyId,
                Provider               = AccountingProvider.Xero,
                TenantId               = tenant.TenantId,
                TenantName             = tenant.TenantName,
                AccessTokenEncrypted   = Encrypt(tokenResponse.AccessToken),
                RefreshTokenEncrypted  = Encrypt(tokenResponse.RefreshToken),
                TokenExpiresAt         = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                IsActive               = true
            };

            _db.AccountingConnections.Add(connection);
            await _db.SaveChangesAsync();

            return ServiceResult<AccountingConnectionResponse>.Success(new AccountingConnectionResponse
            {
                Id           = connection.Id,
                Provider     = "Xero",
                TenantName   = connection.TenantName,
                IsActive     = true,
                ConnectedAt  = connection.ConnectedAt
            }, 201);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Xero OAuth callback failed for company {Id}", companyId);
            return ServiceResult<AccountingConnectionResponse>.Fail(
                $"Xero connection failed: {ex.Message}", 502);
        }
    }

    public async Task<ServiceResult<AccountingConnectionResponse>> GetConnectionAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<AccountingConnectionResponse>.Fail("Access denied.", 403);

        var conn = await _db.AccountingConnections
            .FirstOrDefaultAsync(c => c.CompanyId == companyId &&
                                       c.Provider == AccountingProvider.Xero && c.IsActive);

        if (conn == null) return ServiceResult<AccountingConnectionResponse>.Fail("No Xero connection found.", 404);

        return ServiceResult<AccountingConnectionResponse>.Success(new AccountingConnectionResponse
        {
            Id            = conn.Id,
            Provider      = "Xero",
            TenantName    = conn.TenantName,
            IsActive      = conn.IsActive,
            ConnectedAt   = conn.ConnectedAt,
            LastSyncAt    = conn.LastSyncAt,
            LastSyncError = conn.LastSyncError
        });
    }

    public async Task<ServiceResult> DisconnectAsync(Guid companyId, Guid userId)
    {
        bool isAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == userId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isAdmin) return ServiceResult.Fail("Only owners/admins can disconnect integrations.", 403);

        await _db.AccountingConnections
            .Where(c => c.CompanyId == companyId && c.Provider == AccountingProvider.Xero)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));

        return ServiceResult.Success(204);
    }

    // ── Invoice Sync ──────────────────────────────────────────────────────────

    public async Task<ServiceResult<SyncResultResponse>> SyncInvoicesAsync(
        Guid companyId, SyncInvoiceRequest request, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<SyncResultResponse>.Fail("Access denied.", 403);

        var conn = await _db.AccountingConnections
            .FirstOrDefaultAsync(c => c.CompanyId == companyId &&
                                       c.Provider == AccountingProvider.Xero && c.IsActive);

        if (conn == null) return ServiceResult<SyncResultResponse>.Fail("No active Xero connection. Connect first.", 409);

        // Ensure token is fresh
        await EnsureTokenFreshAsync(conn);

        // Load invoices to sync
        var query = _db.Invoices
            .Include(i => i.Vendor)
            .Include(i => i.InvoiceItems)
            .Where(i => i.CompanyId == companyId && i.Status == InvoiceStatus.Approved);

        if (request.InvoiceIds?.Any() == true)
            query = query.Where(i => request.InvoiceIds.Contains(i.Id));

        var invoices = await query.ToListAsync();

        // Get already-synced IDs to skip
        var syncedIds = await _db.InvoiceSyncRecords
            .Where(r => r.AccountingConnectionId == conn.Id && r.Status == SyncStatus.Synced)
            .Select(r => r.InvoiceId)
            .ToListAsync();

        var result = new SyncResultResponse();
        var accessToken = Decrypt(conn.AccessTokenEncrypted);

        foreach (var invoice in invoices)
        {
            if (syncedIds.Contains(invoice.Id))
            {
                result.Skipped++;
                result.Items.Add(new SyncItemResult
                {
                    InvoiceId     = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber,
                    Status        = "Skipped — already synced"
                });
                continue;
            }

            try
            {
                var xeroId = await PushBillToXeroAsync(invoice, conn.TenantId, accessToken);

                var syncRecord = new InvoiceSyncRecord
                {
                    InvoiceId              = invoice.Id,
                    AccountingConnectionId = conn.Id,
                    ExternalId             = xeroId,
                    ExternalNumber         = invoice.InvoiceNumber ?? invoice.FileName,
                    Status                 = SyncStatus.Synced
                };

                _db.InvoiceSyncRecords.Add(syncRecord);
                result.Synced++;
                result.Items.Add(new SyncItemResult
                {
                    InvoiceId     = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber,
                    Status        = "Synced",
                    ExternalId    = xeroId
                });
            }
            catch (Exception ex)
            {
                _db.InvoiceSyncRecords.Add(new InvoiceSyncRecord
                {
                    InvoiceId              = invoice.Id,
                    AccountingConnectionId = conn.Id,
                    ExternalId             = "",
                    ExternalNumber         = invoice.InvoiceNumber ?? invoice.FileName,
                    Status                 = SyncStatus.Failed,
                    ErrorMessage           = ex.Message
                });

                result.Failed++;
                result.Items.Add(new SyncItemResult
                {
                    InvoiceId     = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber,
                    Status        = "Failed",
                    Error         = ex.Message
                });

                _logger.LogWarning(ex, "Xero sync failed for invoice {Id}", invoice.Id);
            }
        }

        conn.LastSyncAt    = DateTime.UtcNow;
        conn.LastSyncError = result.Failed > 0 ? $"{result.Failed} invoices failed" : null;
        await _db.SaveChangesAsync();

        return ServiceResult<SyncResultResponse>.Success(result);
    }

    // ── Xero API helpers ──────────────────────────────────────────────────────

    private async Task<string> PushBillToXeroAsync(Invoice invoice, string tenantId, string accessToken)
    {
        var client = _http.CreateClient("XeroClient");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("xero-tenant-id", tenantId);
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        var lineItems = invoice.InvoiceItems.Any()
            ? invoice.InvoiceItems.Select(item => new
            {
                Description = item.Description,
                Quantity    = item.Quantity,
                UnitAmount  = item.UnitPrice,
                TaxAmount   = item.TaxRate.HasValue
                    ? Math.Round(item.LineTotal * item.TaxRate.Value / 100, 2)
                    : (decimal?)null,
                AccountCode = "429" // Xero default purchases account
            }).ToList<object>()
            : new List<object>
            {
                new
                {
                    Description = invoice.FileName,
                    Quantity    = 1m,
                    UnitAmount  = invoice.SubTotal ?? invoice.TotalAmount ?? 0,
                    TaxAmount   = invoice.TaxAmount,
                    AccountCode = "429"
                }
            };

        var payload = new
        {
            Invoices = new[]
            {
                new
                {
                    Type             = "ACCPAY",     // accounts payable = Bill
                    Contact          = new { Name = invoice.Vendor?.Name ?? invoice.ExtractedVendorName ?? "Unknown Vendor" },
                    Date             = invoice.InvoiceDate?.ToString("yyyy-MM-dd") ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    DueDate          = invoice.DueDate?.ToString("yyyy-MM-dd"),
                    InvoiceNumber    = invoice.InvoiceNumber,
                    Reference        = invoice.FileName,
                    CurrencyCode     = invoice.Currency ?? "GBP",
                    AmountDue        = invoice.TotalAmount,
                    Status           = "AUTHORISED",
                    LineItems        = lineItems
                }
            }
        };

        var response = await client.PostAsync(
            "https://api.xero.com/api.xro/2.0/Invoices",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();

        var json   = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var xeroId = json.RootElement
            .GetProperty("Invoices")[0]
            .GetProperty("InvoiceID")
            .GetString()
            ?? throw new Exception("Xero did not return an InvoiceID.");

        return xeroId;
    }

    private async Task EnsureTokenFreshAsync(AccountingConnection conn)
    {
        if (DateTime.UtcNow < conn.TokenExpiresAt.AddMinutes(-5)) return;

        var (newAccess, newRefresh, expiresIn) =
            await RefreshXeroTokenAsync(Decrypt(conn.RefreshTokenEncrypted));

        conn.AccessTokenEncrypted  = Encrypt(newAccess);
        conn.RefreshTokenEncrypted = Encrypt(newRefresh);
        conn.TokenExpiresAt        = DateTime.UtcNow.AddSeconds(expiresIn);
        await _db.SaveChangesAsync();
    }

    private async Task<(string AccessToken, string RefreshToken, int ExpiresIn)>
        RefreshXeroTokenAsync(string refreshToken)
    {
        var client = _http.CreateClient("XeroClient");
        var creds  = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ClientId}:{ClientSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);

        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type",    "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        var response = await client.PostAsync("https://identity.xero.com/connect/token", body);
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            json.RootElement.GetProperty("access_token").GetString()!,
            json.RootElement.GetProperty("refresh_token").GetString()!,
            json.RootElement.GetProperty("expires_in").GetInt32()
        );
    }

    private async Task<(string AccessToken, string RefreshToken, int ExpiresIn)>
        ExchangeCodeAsync(string code)
    {
        var client = _http.CreateClient("XeroClient");
        var creds  = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ClientId}:{ClientSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);

        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type",   "authorization_code"),
            new KeyValuePair<string, string>("code",         code),
            new KeyValuePair<string, string>("redirect_uri", RedirectUri)
        });

        var response = await client.PostAsync("https://identity.xero.com/connect/token", body);
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            json.RootElement.GetProperty("access_token").GetString()!,
            json.RootElement.GetProperty("refresh_token").GetString()!,
            json.RootElement.GetProperty("expires_in").GetInt32()
        );
    }

    private async Task<List<(string TenantId, string TenantName)>> GetTenantsAsync(string accessToken)
    {
        var client = _http.CreateClient("XeroClient");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("https://api.xero.com/connections");
        response.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray()
            .Select(t => (
                t.GetProperty("tenantId").GetString()!,
                t.GetProperty("tenantName").GetString()!
            )).ToList();
    }

    // In production replace with proper AES-256 encryption (Azure Key Vault / AWS KMS)
    private string Encrypt(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private string Decrypt(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));
}
