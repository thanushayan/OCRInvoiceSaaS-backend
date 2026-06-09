using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Controllers;

// ══════════════════════════════════════════════════════════════════════════════
// UK Lookups — Companies House & HMRC VAT
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/uk")]
public class UkLookupController : ControllerBase
{
    private readonly CompaniesHouseService _ch;
    private readonly HmrcVatService _vat;

    public UkLookupController(CompaniesHouseService ch, HmrcVatService vat)
    {
        _ch  = ch;
        _vat = vat;
    }

    /// <summary>
    /// Look up a UK company by registration number (e.g. 12345678).
    /// Returns name, registered address, incorporation date, SIC codes, and status.
    /// Use to auto-fill company details when creating a company or vendor.
    /// </summary>
    [HttpGet("companies/{companyNumber}")]
    public async Task<IActionResult> LookupCompany(string companyNumber)
    {
        var result = await _ch.LookupAsync(companyNumber);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Validate a UK VAT number against the HMRC register.
    /// Returns business name and address if valid.
    /// Performs format check (modulus 97) even without HMRC API credentials.
    /// </summary>
    [HttpGet("vat/{vatNumber}")]
    public async Task<IActionResult> ValidateVat(string vatNumber)
    {
        var result = await _vat.ValidateAsync(vatNumber);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Xero Integration
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/integrations/xero")]
public class XeroController : ControllerBase
{
    private readonly XeroSyncService _xero;
    private readonly CurrentUserProvider _currentUser;

    public XeroController(XeroSyncService xero, CurrentUserProvider currentUser)
    {
        _xero        = xero;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Start the Xero OAuth flow. Redirects to Xero's authorisation page.
    /// After the user authorises, Xero calls back to POST /callback with a code.
    /// </summary>
    [HttpGet("connect")]
    public IActionResult Connect(Guid companyId)
    {
        var url = _xero.BuildAuthUrl(companyId);
        return Redirect(url);
    }

    /// <summary>
    /// OAuth callback — exchange the authorisation code for tokens and store connection.
    /// Called automatically by Xero after the user authorises. Returns connection details.
    /// </summary>
    [HttpPost("callback")]
    public async Task<IActionResult> Callback(Guid companyId, [FromBody] XeroOAuthCallbackRequest request)
    {
        var result = await _xero.HandleCallbackAsync(companyId, request.Code, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Get the current Xero connection status for a company.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(Guid companyId)
    {
        var result = await _xero.GetConnectionAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Disconnect Xero. Does not revoke tokens from Xero's side.</summary>
    [HttpDelete("disconnect")]
    public async Task<IActionResult> Disconnect(Guid companyId)
    {
        var result = await _xero.DisconnectAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Push approved invoices to Xero as Bills (accounts payable).
    /// Omit invoiceIds to sync all approved invoices not yet synced.
    /// Already-synced invoices are skipped automatically.
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(Guid companyId, [FromBody] SyncInvoiceRequest request)
    {
        var result = await _xero.SyncInvoicesAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Stripe Webhooks
// ══════════════════════════════════════════════════════════════════════════════

[ApiController]
[Route("api/stripe")]
public class StripeController : ControllerBase
{
    private readonly StripeWebhookService _stripe;

    public StripeController(StripeWebhookService stripe)
    {
        _stripe = stripe;
    }

    /// <summary>
    /// Stripe webhook endpoint. Configure this URL in your Stripe dashboard.
    /// Verifies HMAC-SHA256 signature before processing.
    /// Idempotent — duplicate events are silently ignored.
    ///
    /// Supported events: payment_intent.succeeded, invoice.payment_succeeded,
    ///                   customer.subscription.deleted
    /// </summary>
    [HttpPost("{companyId:guid}")]
    public async Task<IActionResult> Receive(Guid companyId)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody  = await reader.ReadToEndAsync();
        var sig      = Request.Headers["Stripe-Signature"].ToString();

        var result = await _stripe.ReceiveAsync(companyId, rawBody, sig);

        // Stripe requires 2xx to stop retrying
        return result.IsSuccess ? Ok() : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// IP Allowlisting
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/ip-allowlist")]
public class IpAllowlistController : ControllerBase
{
    private readonly IpAllowlistService _service;
    private readonly CurrentUserProvider _currentUser;

    public IpAllowlistController(IpAllowlistService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>List all active IP allowlist entries for a company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId)
    {
        var result = await _service.GetByCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Add an IP address or CIDR range to the company allowlist.
    /// Example CIDRs: 203.0.113.0/24 (range), 203.0.113.42/32 (single IP).
    /// When at least one entry exists, requests from non-listed IPs are blocked (403).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Add(Guid companyId, [FromBody] CreateIpAllowlistRequest request)
    {
        var result = await _service.AddAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Remove an entry from the IP allowlist.</summary>
    [HttpDelete("{entryId:guid}")]
    public async Task<IActionResult> Remove(Guid companyId, Guid entryId)
    {
        var result = await _service.RemoveAsync(entryId, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GDPR & Data Retention
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/gdpr")]
public class GdprController : ControllerBase
{
    private readonly GdprService _gdpr;
    private readonly CurrentUserProvider _currentUser;

    public GdprController(GdprService gdpr, CurrentUserProvider currentUser)
    {
        _gdpr        = gdpr;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Submit a GDPR right-to-erasure request for the authenticated user's personal data.
    /// Invoice records are retained to meet UK legal requirements (7 years).
    /// Personal data (name, email, tokens, login history) will be anonymised.
    /// Requests are reviewed and processed by an admin.
    /// </summary>
    [HttpPost("erasure-request")]
    public async Task<IActionResult> RequestErasure([FromBody] OcrInvoiceSaaS.DTOs.GdprErasureRequest request)
    {
        var result = await _gdpr.RequestErasureAsync(_currentUser.GetUserId(), request);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Process a pending erasure request (admin only).
    /// Anonymises the user's personal data and records what was erased.
    /// </summary>
    [HttpPost("erasure-request/{requestId:guid}/process")]
    public async Task<IActionResult> ProcessErasure(Guid requestId)
    {
        var result = await _gdpr.ProcessErasureAsync(requestId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Invoice Lock
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/invoices/{invoiceId:guid}/lock")]
public class InvoiceLockController : ControllerBase
{
    private readonly InvoiceLockService _service;
    private readonly CurrentUserProvider _currentUser;

    public InvoiceLockController(InvoiceLockService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Get the lock status of an invoice.
    /// Approved invoices are locked automatically to preserve audit integrity.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStatus(Guid invoiceId)
    {
        var result = await _service.GetLockStatusAsync(invoiceId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Unlock an approved invoice for correction (Owner only).
    /// Resets invoice status to Reviewed and requires a documented reason.
    /// The unlock action is logged in the audit trail.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Unlock(Guid invoiceId, [FromBody] UnlockInvoiceRequest request)
    {
        var result = await _service.UnlockAsync(invoiceId, request, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(new { message = "Invoice unlocked. Status reset to Reviewed." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
