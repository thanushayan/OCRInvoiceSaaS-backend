using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

/// <summary>
/// Feature 2 — Multi-step Approval Workflow.
/// Owners/admins define step templates (assignee or role per step);
/// invoices then move through the steps until approved or rejected.
/// </summary>
[Authorize]
[ApiController]
[Route("api")]
public class ApprovalWorkflowController : ControllerBase
{
    private readonly IApprovalWorkflowService _approvals;
    private readonly CurrentUserProvider _currentUser;

    public ApprovalWorkflowController(IApprovalWorkflowService approvals, CurrentUserProvider currentUser)
    {
        _approvals = approvals;
        _currentUser = currentUser;
    }

    // ── Templates ─────────────────────────────────────────────────────────────

    [HttpPost("companies/{companyId:guid}/workflow-templates")]
    public async Task<IActionResult> CreateTemplate(Guid companyId, [FromBody] CreateWorkflowTemplateRequest request)
    {
        var result = await _approvals.CreateTemplateAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("companies/{companyId:guid}/workflow-templates")]
    public async Task<IActionResult> GetTemplates(Guid companyId)
    {
        var result = await _approvals.GetTemplatesAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpDelete("workflow-templates/{templateId:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid templateId)
    {
        var result = await _approvals.DeleteTemplateAsync(templateId, _currentUser.GetUserId());
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    // ── Approval instances ────────────────────────────────────────────────────

    /// <summary>Starts an approval run for an invoice using a template.</summary>
    [HttpPost("invoices/{invoiceId:guid}/approvals")]
    public async Task<IActionResult> Start(Guid invoiceId, [FromBody] StartApprovalRequest request)
    {
        var result = await _approvals.StartApprovalAsync(invoiceId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(201, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Approvals waiting on the current user (by assignment or role).</summary>
    [HttpGet("approvals/pending")]
    public async Task<IActionResult> GetPending()
    {
        var result = await _approvals.GetPendingForUserAsync(_currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("approvals/{instanceId:guid}")]
    public async Task<IActionResult> GetInstance(Guid instanceId)
    {
        var result = await _approvals.GetInstanceAsync(instanceId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Approve or reject the current step.</summary>
    [HttpPost("approvals/{instanceId:guid}/actions")]
    public async Task<IActionResult> SubmitAction(Guid instanceId, [FromBody] SubmitApprovalActionRequest request)
    {
        var result = await _approvals.SubmitActionAsync(instanceId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Cancels an in-flight approval (owners/admins only).</summary>
    [HttpPost("approvals/{instanceId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid instanceId)
    {
        var result = await _approvals.CancelApprovalAsync(instanceId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(new { message = "Approval cancelled." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
