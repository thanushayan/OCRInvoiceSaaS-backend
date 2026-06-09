using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

/// <summary>
/// Feature 6 — Purchase Orders &amp; invoice matching.
/// POs are scored against invoices (vendor, amount, PO number in OCR text);
/// matches ≥80% mark the PO fully matched, 50–79% partially.
/// </summary>
[Authorize]
[ApiController]
[Route("api")]
public class PurchaseOrderController : ControllerBase
{
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly IInvoiceMatchingService _matching;
    private readonly CurrentUserProvider _currentUser;

    public PurchaseOrderController(
        IPurchaseOrderService purchaseOrders,
        IInvoiceMatchingService matching,
        CurrentUserProvider currentUser)
    {
        _purchaseOrders = purchaseOrders;
        _matching = matching;
        _currentUser = currentUser;
    }

    // ── Purchase orders ───────────────────────────────────────────────────────

    [HttpPost("companies/{companyId:guid}/purchase-orders")]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreatePurchaseOrderRequest request)
    {
        var result = await _purchaseOrders.CreateAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("companies/{companyId:guid}/purchase-orders")]
    public async Task<IActionResult> GetForCompany(Guid companyId)
    {
        var result = await _purchaseOrders.GetByCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("purchase-orders/{poId:guid}")]
    public async Task<IActionResult> GetById(Guid poId)
    {
        var result = await _purchaseOrders.GetByIdAsync(poId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpDelete("purchase-orders/{poId:guid}")]
    public async Task<IActionResult> Delete(Guid poId)
    {
        var result = await _purchaseOrders.DeleteAsync(poId, _currentUser.GetUserId());
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    // ── Invoice ↔ PO matching ─────────────────────────────────────────────────

    /// <summary>Scores the invoice against all open POs and stores matches ≥50%.</summary>
    [HttpPost("invoices/{invoiceId:guid}/po-matches/auto")]
    public async Task<IActionResult> AutoMatch(Guid invoiceId)
    {
        var result = await _matching.AutoMatchAsync(invoiceId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Manually confirms a match against a specific PO.</summary>
    [HttpPost("invoices/{invoiceId:guid}/po-matches")]
    public async Task<IActionResult> ManualMatch(Guid invoiceId, [FromBody] MatchInvoiceToPoRequest request)
    {
        var result = await _matching.ManualMatchAsync(invoiceId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("invoices/{invoiceId:guid}/po-matches")]
    public async Task<IActionResult> GetMatches(Guid invoiceId)
    {
        var result = await _matching.GetMatchesForInvoiceAsync(invoiceId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPost("po-matches/{matchId:guid}/dismiss")]
    public async Task<IActionResult> DismissMatch(Guid matchId)
    {
        var result = await _matching.DismissMatchAsync(matchId, _currentUser.GetUserId());
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
