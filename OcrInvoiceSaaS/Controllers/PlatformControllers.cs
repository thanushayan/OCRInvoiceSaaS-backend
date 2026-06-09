using OcrInvoiceSaaS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

// ══════════════════════════════════════════════════════════════════════════════
// Reporting & Export
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookService _service;
    private readonly CurrentUserProvider _currentUser;

    public WebhookController(IWebhookService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Register a new webhook endpoint. Returns the signing secret ONCE.
    /// Use the secret to verify incoming requests via HMAC-SHA256 on the X-OcrInvoice-Signature header.
    /// Omit 'events' to subscribe to all event types.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreateWebhookRequest request)
    {
        var result = await _service.CreateEndpointAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>List all active webhook endpoints. The full signing secret is never returned.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId)
    {
        var result = await _service.GetEndpointsAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Disable and remove a webhook endpoint.</summary>
    [HttpDelete("{endpointId:guid}")]
    public async Task<IActionResult> Delete(Guid companyId, Guid endpointId)
    {
        var result = await _service.DeleteEndpointAsync(endpointId, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>View the delivery history for a webhook endpoint (last 100 deliveries).</summary>
    [HttpGet("{endpointId:guid}/deliveries")]
    public async Task<IActionResult> GetDeliveries(Guid companyId, Guid endpointId)
    {
        var result = await _service.GetDeliveriesAsync(endpointId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Manually retry a specific failed delivery immediately.</summary>
    [HttpPost("{endpointId:guid}/deliveries/{deliveryId:guid}/retry")]
    public async Task<IActionResult> RetryDelivery(Guid companyId, Guid endpointId, Guid deliveryId)
    {
        var result = await _service.RetryDeliveryAsync(deliveryId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(new { message = "Delivery queued for immediate retry." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Returns all valid webhook event types that can be subscribed to.</summary>
    [HttpGet("events")]
    public IActionResult GetEventTypes(Guid companyId)
    {
        var events = Enum.GetNames<OcrInvoiceSaaS.Models.WebhookEvent>()
            .Select(e => new
            {
                name        = e,
                description = EventDescription(e)
            });
        return Ok(events);
    }

    private static string EventDescription(string eventName) => eventName switch
    {
        "InvoiceCreated"      => "Fired when a new invoice record is created",
        "InvoiceOcrCompleted" => "Fired when OCR extraction finishes (success or failure)",
        "InvoiceStatusChanged"=> "Fired on any invoice status transition",
        "InvoiceApproved"     => "Fired when the final approval step is completed",
        "InvoiceRejected"     => "Fired when an approver rejects an invoice",
        "DuplicateFlagged"    => "Fired when a duplicate invoice is detected",
        "BulkOcrCompleted"    => "Fired when an entire bulk OCR job finishes",
        "PaymentRecorded"     => "Fired when a payment is recorded against a subscription",
        "ApprovalRequested"   => "Fired when an approval workflow step is awaiting action",
        _                     => ""
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// Invoice Activity & Comments
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/invoices/{invoiceId:guid}/activity")]
public class ActivityController : ControllerBase
{
    private readonly IActivityService _service;
    private readonly CurrentUserProvider _currentUser;

    public ActivityController(IActivityService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Get the full activity feed for an invoice — includes both user comments
    /// and system-generated events (OCR completed, status changed, approval actions, etc.)
    /// ordered newest first.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid invoiceId)
    {
        var result = await _service.GetForInvoiceAsync(invoiceId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Add a comment to an invoice.</summary>
    [HttpPost]
    public async Task<IActionResult> AddComment(Guid invoiceId, [FromBody] AddCommentRequest request)
    {
        var result = await _service.AddCommentAsync(invoiceId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Delete a comment. Users can delete their own; owners/admins can delete any.</summary>
    [HttpDelete("{activityId:guid}")]
    public async Task<IActionResult> DeleteComment(Guid invoiceId, Guid activityId)
    {
        var result = await _service.DeleteCommentAsync(activityId, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Rate Limiting Status
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/rate-limit")]
public class RateLimitController : ControllerBase
{
    private readonly IRateLimitService _service;
    private readonly CurrentUserProvider _currentUser;

    public RateLimitController(IRateLimitService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Check the current rate limit usage for a company.
    /// Shows requests used, limit, remaining, and when the window resets.
    /// Limits are based on the active subscription plan.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStatus(Guid companyId)
    {
        var result = await _service.GetStatusAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
