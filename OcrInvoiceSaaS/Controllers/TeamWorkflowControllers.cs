using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

// ══════════════════════════════════════════════════════════════════════════════
// @Mention Notifications
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/me/mentions")]
public class MentionController : ControllerBase
{
    private readonly IMentionService _service;
    private readonly CurrentUserProvider _currentUser;

    public MentionController(IMentionService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Get all unread @mentions for the current user across all companies (max 50).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUnread()
    {
        var result = await _service.GetUnreadMentionsAsync(_currentUser.GetUserId());
        return Ok(result.Data);
    }

    /// <summary>Count of unread @mentions — useful for notification badge.</summary>
    [HttpGet("count")]
    public async Task<IActionResult> GetCount()
    {
        var count = await _service.GetUnreadCountAsync(_currentUser.GetUserId());
        return Ok(new { unreadCount = count });
    }

    /// <summary>
    /// Mark mentions as read. POST with an empty body (or omit 'ids') to mark all read.
    /// POST with 'ids' array to mark specific mentions only.
    /// </summary>
    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead([FromBody] MarkMentionsReadRequest request)
    {
        var result = await _service.MarkMentionsReadAsync(_currentUser.GetUserId(), request.Ids);
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

/// <summary>Request body for marking specific mentions read.</summary>
public class MarkMentionsReadRequest
{
    public List<Guid>? Ids { get; set; }   // null = mark all
}

// ══════════════════════════════════════════════════════════════════════════════
// Invoice Comments with @Mentions
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/invoices/{invoiceId:guid}/comments")]
public class InvoiceCommentController : ControllerBase
{
    private readonly IMentionService _mentionService;
    private readonly CurrentUserProvider _currentUser;

    public InvoiceCommentController(IMentionService mentionService, CurrentUserProvider currentUser)
    {
        _mentionService = mentionService;
        _currentUser    = currentUser;
    }

    /// <summary>
    /// Post a comment that supports @mention tokens.
    ///
    /// Two ways to mention teammates:
    ///   1. Include their userIds in the 'mentionedUserIds' array.
    ///   2. Embed @{userId} tokens directly in the comment text for rich-text clients.
    ///
    /// Mentioned users receive an in-app notification and an email.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> AddComment(
        Guid invoiceId, [FromBody] AddCommentWithMentionsRequest request)
    {
        var result = await _mentionService.AddCommentWithMentionsAsync(
            invoiceId, request, _currentUser.GetUserId());

        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Task Assignment
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/invoices/{invoiceId:guid}/tasks")]
public class InvoiceTaskController : ControllerBase
{
    private readonly ITaskService _taskService;
    private readonly CurrentUserProvider _currentUser;

    public InvoiceTaskController(ITaskService taskService, CurrentUserProvider currentUser)
    {
        _taskService = taskService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Assign a task to a team member on this invoice.
    /// The assignee receives an in-app notification and an email.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid invoiceId, [FromBody] CreateTaskRequest request)
    {
        var result = await _taskService.CreateAsync(invoiceId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>List all tasks on a specific invoice.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid invoiceId)
    {
        var result = await _taskService.GetForInvoiceAsync(invoiceId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

[Authorize]
[ApiController]
[Route("api/tasks")]
public class TaskController : ControllerBase
{
    private readonly ITaskService _taskService;
    private readonly CurrentUserProvider _currentUser;

    public TaskController(ITaskService taskService, CurrentUserProvider currentUser)
    {
        _taskService = taskService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Get all tasks assigned to the current user across all companies.
    /// Filter by status, priority, overdue.
    /// </summary>
    [HttpGet("assigned-to-me")]
    public async Task<IActionResult> GetMyTasks([FromQuery] TaskFilterRequest filter)
    {
        var result = await _taskService.GetAssignedToUserAsync(_currentUser.GetUserId(), filter);
        return Ok(result.Data);
    }

    /// <summary>Update a task — status, priority, due date, reassign, completion note.</summary>
    [HttpPatch("{taskId:guid}")]
    public async Task<IActionResult> Update(Guid taskId, [FromBody] UpdateTaskRequest request)
    {
        var result = await _taskService.UpdateAsync(taskId, request, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Delete a task. Only the original assigner or an admin/owner can delete.</summary>
    [HttpDelete("{taskId:guid}")]
    public async Task<IActionResult> Delete(Guid taskId)
    {
        var result = await _taskService.DeleteAsync(taskId, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/tasks")]
public class CompanyTaskController : ControllerBase
{
    private readonly ITaskService _taskService;
    private readonly CurrentUserProvider _currentUser;

    public CompanyTaskController(ITaskService taskService, CurrentUserProvider currentUser)
    {
        _taskService = taskService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// List all tasks across the company with optional filters.
    /// Useful for team leads to monitor workload distribution.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId, [FromQuery] TaskFilterRequest filter)
    {
        var result = await _taskService.GetForCompanyAsync(companyId, filter, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Approval Delegation
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/delegations")]
public class DelegationController : ControllerBase
{
    private readonly IDelegationService _service;
    private readonly CurrentUserProvider _currentUser;

    public DelegationController(IDelegationService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Create an approval delegation (out-of-office cover).
    /// While active, any approval step assigned to you is automatically
    /// rerouted to the delegate. You receive an email confirmation;
    /// the delegate receives a notification and email.
    ///
    /// Optional: limit the delegation to a specific workflow template.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreateDelegationRequest request)
    {
        var result = await _service.CreateAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// List all delegations (past and present) where you are the delegator or delegate.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMine(Guid companyId)
    {
        var result = await _service.GetForUserAsync(_currentUser.GetUserId(), companyId);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// List all currently active delegations across the company.
    /// Visible to all members — useful for seeing who is covering whom.
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetActive(Guid companyId)
    {
        var result = await _service.GetActiveForCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Revoke a delegation immediately.
    /// Only the delegator or an admin/owner can revoke.
    /// The delegate is notified.
    /// </summary>
    [HttpDelete("{delegationId:guid}")]
    public async Task<IActionResult> Revoke(Guid companyId, Guid delegationId)
    {
        var result = await _service.RevokeAsync(delegationId, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
