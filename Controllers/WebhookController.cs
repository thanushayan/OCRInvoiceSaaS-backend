using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

/// <summary>
/// Feature 5 — Webhooks.
/// Companies register endpoints and receive signed POSTs on invoice events.
/// The signing secret is returned exactly once at creation; verify requests
/// with HMAC-SHA256 keyed on the SHA-256 hex of that secret
/// (header: X-Webhook-Signature: t=&lt;ts&gt;,v1=&lt;hex&gt;).
/// </summary>
[Authorize]
[ApiController]
[Route("api")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookService _webhooks;
    private readonly CurrentUserProvider _currentUser;

    public WebhookController(IWebhookService webhooks, CurrentUserProvider currentUser)
    {
        _webhooks = webhooks;
        _currentUser = currentUser;
    }

    [HttpPost("companies/{companyId:guid}/webhooks")]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreateWebhookRequest request)
    {
        var result = await _webhooks.CreateEndpointAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("companies/{companyId:guid}/webhooks")]
    public async Task<IActionResult> GetForCompany(Guid companyId)
    {
        var result = await _webhooks.GetEndpointsAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpDelete("webhooks/{endpointId:guid}")]
    public async Task<IActionResult> Delete(Guid endpointId)
    {
        var result = await _webhooks.DeleteEndpointAsync(endpointId, _currentUser.GetUserId());
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Last 50 delivery attempts for an endpoint.</summary>
    [HttpGet("webhooks/{endpointId:guid}/deliveries")]
    public async Task<IActionResult> GetDeliveries(Guid endpointId)
    {
        var result = await _webhooks.GetDeliveriesAsync(endpointId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Re-queues a failed delivery; picked up within 5 minutes.</summary>
    [HttpPost("webhook-deliveries/{deliveryId:guid}/retry")]
    public async Task<IActionResult> RetryDelivery(Guid deliveryId)
    {
        var result = await _webhooks.RetryDeliveryAsync(deliveryId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Accepted(new { message = "Delivery re-queued." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
