using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _service;
    private readonly CurrentUserProvider _currentUser;

    public SubscriptionController(ISubscriptionService service, CurrentUserProvider currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [AllowAnonymous]
    [HttpGet("api/subscription-plans")]
    public async Task<IActionResult> GetPlans()
    {
        var result = await _service.GetPlansAsync();
        return Ok(result.Data);
    }

    [HttpPost("api/companies/{companyId:guid}/subscription")]
    public async Task<IActionResult> Subscribe(Guid companyId, [FromBody] CreateCompanySubscriptionRequest request)
    {
        var result = await _service.SubscribeAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("api/companies/{companyId:guid}/subscription")]
    public async Task<IActionResult> GetActive(Guid companyId)
    {
        var result = await _service.GetActiveSubscriptionAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPost("api/subscriptions/{subscriptionId:guid}/payments")]
    public async Task<IActionResult> RecordPayment(Guid subscriptionId, [FromBody] RecordPaymentRequest request)
    {
        var result = await _service.RecordPaymentAsync(subscriptionId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
