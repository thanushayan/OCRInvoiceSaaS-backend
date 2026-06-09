using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/invoices")]
public class InvoiceControllerV2 : ControllerBase
{
    private readonly InvoiceServiceV2 _invoiceService;
    private readonly IOcrService _ocrService;
    private readonly CurrentUserProvider _currentUser;

    public InvoiceControllerV2(InvoiceServiceV2 invoiceService, IOcrService ocrService, CurrentUserProvider currentUser)
    {
        _invoiceService = invoiceService;
        _ocrService = ocrService;
        _currentUser = currentUser;
    }

    /// <summary>Upload / register a new invoice record.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreateInvoiceRequest request)
    {
        var userId = _currentUser.GetUserId();
        var result = await _invoiceService.CreateInvoiceAsync(companyId, request, userId);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Paginated invoice list with optional search, status filter, and sorting.
    /// Query params: page, pageSize, search, status, sortBy (createdAt|amount|filename|status), sortDir (asc|desc)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId, [FromQuery] PaginationQuery query)
    {
        var userId = _currentUser.GetUserId();
        var result = await _invoiceService.GetInvoicesPagedAsync(companyId, userId, query);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Get a single invoice with full details and line items.</summary>
    [HttpGet("{invoiceId:guid}")]
    public async Task<IActionResult> GetById(Guid companyId, Guid invoiceId)
    {
        var userId = _currentUser.GetUserId();
        var result = await _invoiceService.GetInvoiceByIdAsync(invoiceId, userId);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Update extracted fields or advance the status of an invoice.</summary>
    [HttpPatch("{invoiceId:guid}")]
    public async Task<IActionResult> Update(Guid companyId, Guid invoiceId, [FromBody] UpdateInvoiceRequest request)
    {
        var userId = _currentUser.GetUserId();
        var result = await _invoiceService.UpdateInvoiceAsync(invoiceId, request, userId);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Trigger OCR extraction on a previously uploaded invoice.</summary>
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
