using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/invoices")]
public class InvoiceController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;
    private readonly IOcrService _ocrService;
    private readonly CurrentUserProvider _currentUser;

    public InvoiceController(IInvoiceService invoiceService, IOcrService ocrService, CurrentUserProvider currentUser)
    {
        _invoiceService = invoiceService;
        _ocrService = ocrService;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreateInvoiceRequest request)
    {
        var userId = _currentUser.GetUserId();
        var result = await _invoiceService.CreateInvoiceAsync(companyId, request, userId);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId)
    {
        var userId = _currentUser.GetUserId();
        var result = await _invoiceService.GetInvoicesAsync(companyId, userId);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("{invoiceId:guid}")]
    public async Task<IActionResult> GetById(Guid companyId, Guid invoiceId)
    {
        var userId = _currentUser.GetUserId();
        var result = await _invoiceService.GetInvoiceByIdAsync(invoiceId, userId);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPatch("{invoiceId:guid}")]
    public async Task<IActionResult> Update(Guid companyId, Guid invoiceId, [FromBody] UpdateInvoiceRequest request)
    {
        var userId = _currentUser.GetUserId();
        var result = await _invoiceService.UpdateInvoiceAsync(invoiceId, request, userId);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPost("{invoiceId:guid}/ocr")]
    public async Task<IActionResult> RunOcr(Guid companyId, Guid invoiceId)
    {
        var userId = _currentUser.GetUserId();
        var result = await _ocrService.ProcessInvoiceAsync(invoiceId, userId);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
