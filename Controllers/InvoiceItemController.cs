using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/invoices/{invoiceId:guid}/items")]
public class InvoiceItemController : ControllerBase
{
    private readonly IInvoiceItemService _service;
    private readonly CurrentUserProvider _currentUser;

    public InvoiceItemController(IInvoiceItemService service, CurrentUserProvider currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Add(Guid invoiceId, [FromBody] AddInvoiceItemRequest request)
    {
        var result = await _service.AddItemAsync(invoiceId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPatch("{itemId:guid}")]
    public async Task<IActionResult> Update(Guid invoiceId, Guid itemId, [FromBody] UpdateInvoiceItemRequest request)
    {
        var result = await _service.UpdateItemAsync(itemId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpDelete("{itemId:guid}")]
    public async Task<IActionResult> Delete(Guid invoiceId, Guid itemId)
    {
        var result = await _service.DeleteItemAsync(itemId, _currentUser.GetUserId());
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
