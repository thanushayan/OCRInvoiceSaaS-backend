using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

// ══════════════════════════════════════════════════════════════════════════════
// Feature 1 — Duplicate Detection
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/duplicates")]
public class DuplicateController : ControllerBase
{
    private readonly IDuplicateDetectionService _service;
    private readonly CurrentUserProvider _currentUser;

    public DuplicateController(IDuplicateDetectionService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>List all unresolved duplicate flags for a company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId)
    {
        var result = await _service.GetFlagsForCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Get duplicate flags for a specific invoice.</summary>
    [HttpGet("invoice/{invoiceId:guid}")]
    public async Task<IActionResult> GetForInvoice(Guid companyId, Guid invoiceId)
    {
        var result = await _service.GetFlagsForInvoiceAsync(invoiceId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Review a duplicate flag — set status to Dismissed or Confirmed.
    /// Dismissed = false positive, remove the flag.
    /// Confirmed = real duplicate, analyst will handle accordingly.
    /// </summary>
    [HttpPost("{flagId:guid}/review")]
    public async Task<IActionResult> Review(Guid companyId, Guid flagId, [FromBody] ReviewDuplicateRequest request)
    {
        var result = await _service.ReviewFlagAsync(flagId, request, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(new { message = "Flag reviewed." }) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 2 — Approval Workflow
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
public class ApprovalWorkflowController : ControllerBase
{
    private readonly IApprovalWorkflowService _service;
    private readonly CurrentUserProvider _currentUser;

    public ApprovalWorkflowController(IApprovalWorkflowService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    // ── Templates ──────────────────────────────────────────────────────────

    /// <summary>Create a new approval workflow template for a company.</summary>
    [HttpPost("api/companies/{companyId:guid}/approval-templates")]
    public async Task<IActionResult> CreateTemplate(Guid companyId, [FromBody] CreateWorkflowTemplateRequest request)
    {
        var result = await _service.CreateTemplateAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>List all active workflow templates for a company.</summary>
    [HttpGet("api/companies/{companyId:guid}/approval-templates")]
    public async Task<IActionResult> GetTemplates(Guid companyId)
    {
        var result = await _service.GetTemplatesAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Soft-delete a workflow template.</summary>
    [HttpDelete("api/companies/{companyId:guid}/approval-templates/{templateId:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid companyId, Guid templateId)
    {
        var result = await _service.DeleteTemplateAsync(templateId, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }

    // ── Instances ──────────────────────────────────────────────────────────

    /// <summary>Start an approval workflow on an invoice.</summary>
    [HttpPost("api/invoices/{invoiceId:guid}/approval")]
    public async Task<IActionResult> StartApproval(Guid invoiceId, [FromBody] StartApprovalRequest request)
    {
        var result = await _service.StartApprovalAsync(invoiceId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Get the current state of an approval workflow instance.</summary>
    [HttpGet("api/approvals/{instanceId:guid}")]
    public async Task<IActionResult> GetInstance(Guid instanceId)
    {
        var result = await _service.GetInstanceAsync(instanceId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Get all approval instances currently pending the authenticated user's action.</summary>
    [HttpGet("api/approvals/pending")]
    public async Task<IActionResult> GetPending()
    {
        var result = await _service.GetPendingForUserAsync(_currentUser.GetUserId());
        return Ok(result.Data);
    }

    /// <summary>Approve or reject the current step on an approval instance.</summary>
    [HttpPost("api/approvals/{instanceId:guid}/action")]
    public async Task<IActionResult> SubmitAction(Guid instanceId, [FromBody] SubmitApprovalActionRequest request)
    {
        var result = await _service.SubmitActionAsync(instanceId, request, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Cancel an in-progress approval workflow (Owner / Admin only).</summary>
    [HttpPost("api/approvals/{instanceId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid instanceId)
    {
        var result = await _service.CancelApprovalAsync(instanceId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(new { message = "Approval cancelled." }) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 3 — Bulk OCR Processing
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/bulk-ocr")]
public class BulkOcrController : ControllerBase
{
    private readonly IBulkOcrService _service;
    private readonly CurrentUserProvider _currentUser;

    public BulkOcrController(IBulkOcrService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Queue multiple invoices for OCR processing in a single job.
    /// Maximum 100 invoices per job. Only Uploaded or Failed invoices are eligible.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateJob(Guid companyId, [FromBody] CreateBulkOcrJobRequest request)
    {
        var result = await _service.CreateJobAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>List all bulk OCR jobs for a company (most recent 50).</summary>
    [HttpGet]
    public async Task<IActionResult> GetJobs(Guid companyId)
    {
        var result = await _service.GetJobsForCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Get the live status of a specific bulk OCR job including per-invoice results.</summary>
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetJob(Guid companyId, Guid jobId)
    {
        var result = await _service.GetJobAsync(jobId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Cancel a queued or in-progress bulk OCR job.</summary>
    [HttpPost("{jobId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid companyId, Guid jobId)
    {
        var result = await _service.CancelJobAsync(jobId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(new { message = "Job cancelled." }) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 4 — Purchase Orders & Matching
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/purchase-orders")]
public class PurchaseOrderController : ControllerBase
{
    private readonly IPurchaseOrderService _poService;
    private readonly IInvoiceMatchingService _matchService;
    private readonly CurrentUserProvider _currentUser;

    public PurchaseOrderController(
        IPurchaseOrderService poService,
        IInvoiceMatchingService matchService,
        CurrentUserProvider currentUser)
    {
        _poService    = poService;
        _matchService = matchService;
        _currentUser  = currentUser;
    }

    /// <summary>Create a new purchase order.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreatePurchaseOrderRequest request)
    {
        var result = await _poService.CreateAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>List all active purchase orders for a company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId)
    {
        var result = await _poService.GetByCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Get a single PO with line items and current match status.</summary>
    [HttpGet("{poId:guid}")]
    public async Task<IActionResult> GetById(Guid companyId, Guid poId)
    {
        var result = await _poService.GetByIdAsync(poId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Cancel / soft-delete a purchase order (Owner / Admin only).</summary>
    [HttpDelete("{poId:guid}")]
    public async Task<IActionResult> Delete(Guid companyId, Guid poId)
    {
        var result = await _poService.DeleteAsync(poId, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Update PO details, status or line items.</summary>
    [HttpPut("{poId:guid}")]
    public async Task<IActionResult> Update(Guid companyId, Guid poId, [FromBody] UpdatePurchaseOrderRequest request)
    {
        var result = await _poService.UpdateAsync(poId, request, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

[Authorize]
[ApiController]
[Route("api/invoices/{invoiceId:guid}/po-matches")]
public class InvoiceMatchingController : ControllerBase
{
    private readonly IInvoiceMatchingService _service;
    private readonly CurrentUserProvider _currentUser;

    public InvoiceMatchingController(IInvoiceMatchingService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>Auto-match this invoice against all open POs. Scores are calculated automatically.</summary>
    [HttpPost("auto")]
    public async Task<IActionResult> AutoMatch(Guid invoiceId)
    {
        var result = await _service.AutoMatchAsync(invoiceId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Manually link this invoice to a specific purchase order.</summary>
    [HttpPost]
    public async Task<IActionResult> ManualMatch(Guid invoiceId, [FromBody] MatchInvoiceToPoRequest request)
    {
        var result = await _service.ManualMatchAsync(invoiceId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>List all PO matches for this invoice.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMatches(Guid invoiceId)
    {
        var result = await _service.GetMatchesForInvoiceAsync(invoiceId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Dismiss an auto-suggested match as a false positive.</summary>
    [HttpDelete("{matchId:guid}")]
    public async Task<IActionResult> Dismiss(Guid invoiceId, Guid matchId)
    {
        var result = await _service.DismissMatchAsync(matchId, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 5 — Currency Conversion
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/currency")]
public class CurrencyController : ControllerBase
{
    private readonly ICurrencyConversionService _service;

    public CurrencyController(ICurrencyConversionService service) => _service = service;

    /// <summary>Get the live or cached exchange rate between two currencies.</summary>
    [HttpGet("rate")]
    public async Task<IActionResult> GetRate([FromQuery] string from, [FromQuery] string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "Both 'from' and 'to' query parameters are required." });

        var result = await _service.GetRateAsync(from, to);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Convert an amount between two currencies using the current rate.</summary>
    [HttpPost("convert")]
    public async Task<IActionResult> Convert([FromBody] ConvertRequest request)
    {
        var result = await _service.ConvertAsync(request.From, request.To, request.Amount);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>List all rates currently cached against the given base currency.</summary>
    [HttpGet("rates/{baseCurrency}")]
    public async Task<IActionResult> GetCachedRates(string baseCurrency)
    {
        var result = await _service.GetCachedRatesAsync(baseCurrency);
        return Ok(result.Data);
    }
}

public class ConvertRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public string From { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    public string To { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }
}
