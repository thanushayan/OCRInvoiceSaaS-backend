using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Interfaces;

public interface IMentionService
{
    /// <summary>
    /// Post a comment that may contain @mention tokens or an explicit mentionedUserIds list.
    /// Fires in-app notifications and emails for each mentioned user.
    /// </summary>
    Task<ServiceResult<ActivityResponse>> AddCommentWithMentionsAsync(
        Guid invoiceId, AddCommentWithMentionsRequest request, Guid userId);

    /// <summary>All unread @mentions for the current user across all companies.</summary>
    Task<ServiceResult<List<MentionResponse>>> GetUnreadMentionsAsync(Guid userId);

    /// <summary>Mark specific mentions (or all) as read.</summary>
    Task<ServiceResult> MarkMentionsReadAsync(Guid userId, List<Guid>? mentionIds = null);

    Task<int> GetUnreadCountAsync(Guid userId);
}

public interface ITaskService
{
    Task<ServiceResult<TaskResponse>> CreateAsync(
        Guid invoiceId, CreateTaskRequest request, Guid assignedByUserId);

    Task<ServiceResult<TaskResponse>> UpdateAsync(
        Guid taskId, UpdateTaskRequest request, Guid userId);

    Task<ServiceResult<List<TaskResponse>>> GetForInvoiceAsync(Guid invoiceId, Guid userId);

    Task<ServiceResult<List<TaskResponse>>> GetAssignedToUserAsync(
        Guid userId, TaskFilterRequest filter);

    Task<ServiceResult<List<TaskResponse>>> GetForCompanyAsync(
        Guid companyId, TaskFilterRequest filter, Guid userId);

    Task<ServiceResult> DeleteAsync(Guid taskId, Guid userId);

    /// <summary>Called by Quartz job — notify users of overdue tasks.</summary>
    Task NotifyOverdueTasksAsync();
}

public interface IDelegationService
{
    Task<ServiceResult<DelegationResponse>> CreateAsync(
        Guid companyId, CreateDelegationRequest request, Guid delegatorUserId);

    Task<ServiceResult<List<DelegationResponse>>> GetForUserAsync(Guid userId, Guid companyId);

    Task<ServiceResult<List<DelegationResponse>>> GetActiveForCompanyAsync(Guid companyId, Guid userId);

    Task<ServiceResult> RevokeAsync(Guid delegationId, Guid userId);

    /// <summary>
    /// Resolves the effective approver for a workflow step, respecting active delegations.
    /// Returns the delegate's userId if an active delegation covers this step, otherwise the original.
    /// </summary>
    Task<Guid> ResolveEffectiveApproverAsync(Guid originalUserId, Guid companyId, Guid? workflowTemplateId);
}
