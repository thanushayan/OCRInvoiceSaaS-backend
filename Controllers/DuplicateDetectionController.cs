using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

/// <summary>
/// Feature 1 — Duplicate Detection.
/// Invoices are scored against existing ones (invoice number, vendor,
/// amount, date); flags ≥60% are surfaced here for review.
/// </summary>
[Authorize]
[ApiController]
[Route("api")]
public class DuplicateDetectionController : ControllerBase
{
    private readonly IDuplicateDetectionService _duplicates;
    private readonly IInvoiceService _invoices;
    private readonly CurrentUserProvider _currentUser;

    public DuplicateDetectionController(
        IDuplicateDetectionService duplicates,
        IInvoiceService invoices,
        CurrentUserProvider currentUser)
    {
        _duplicates = duplicates;
        _invoices = invoices;
        _currentUser = currentUser;
    }

    /// <summary>All open duplicate flags for a company, highest confidence first.</summary>
    [HttpGet("companies/{companyId:guid}/duplicates")]
    public async Task<IActionResult> GetForCompany(Guid companyId)
    {
        var result = await _duplicates.GetFlagsForCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Duplicate flags raised against a single invoice.</summary>
    [HttpGet("invoices/{invoiceId:guid}/duplicates")]
    public async Task<IActionResult> GetForInvoice(Guid invoiceId)
    {
        var result = await _duplicates.GetFlagsForInvoiceAsync(invoiceId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Runs duplicate detection on an invoice on demand.</summary>
    [HttpPost("companies/{companyId:guid}/invoices/{invoiceId:guid}/check-duplicates")]
    public async Task<IActionResult> Check(Guid companyId, Guid invoiceId)
    {
        var userId = _currentUser.GetUserId();

        // Membership/ownership gate — CheckAsync itself is tenant-blind.
        var invoice = await _invoices.GetInvoiceByIdAsync(invoiceId, userId);
        if (!invoice.IsSuccess)
            return StatusCode(invoice.StatusCode, new { error = invoice.Error });

        var result = await _duplicates.CheckAsync(invoiceId, companyId);
        return Ok(result);
    }

    /// <summary>Resolves a flag as Dismissed (false positive) or Confirmed.</summary>
    [HttpPost("duplicates/{duplicateFlagId:guid}/review")]
    public async Task<IActionResult> Review(Guid duplicateFlagId, [FromBody] ReviewDuplicateRequest request)
    {
        var result = await _duplicates.ReviewFlagAsync(duplicateFlagId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(new { message = "Duplicate flag reviewed." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
