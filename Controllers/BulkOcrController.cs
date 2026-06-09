using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

/// <summary>
/// Feature 3 — Bulk OCR.
/// Queue up to 100 uploaded/failed invoices; BulkOcrProcessingJob works
/// through the queue in batches and progress is polled via GET.
/// </summary>
[Authorize]
[ApiController]
[Route("api")]
public class BulkOcrController : ControllerBase
{
    private readonly IBulkOcrService _bulkOcr;
    private readonly CurrentUserProvider _currentUser;

    public BulkOcrController(IBulkOcrService bulkOcr, CurrentUserProvider currentUser)
    {
        _bulkOcr = bulkOcr;
        _currentUser = currentUser;
    }

    [HttpPost("companies/{companyId:guid}/bulk-ocr")]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreateBulkOcrJobRequest request)
    {
        var result = await _bulkOcr.CreateJobAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("companies/{companyId:guid}/bulk-ocr")]
    public async Task<IActionResult> GetForCompany(Guid companyId)
    {
        var result = await _bulkOcr.GetJobsForCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("bulk-ocr/{jobId:guid}")]
    public async Task<IActionResult> GetJob(Guid jobId)
    {
        var result = await _bulkOcr.GetJobAsync(jobId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Cancels remaining queued items; already-processed ones stand.</summary>
    [HttpPost("bulk-ocr/{jobId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid jobId)
    {
        var result = await _bulkOcr.CancelJobAsync(jobId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(new { message = "Bulk OCR job cancelled." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
