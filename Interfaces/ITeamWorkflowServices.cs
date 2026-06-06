using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Interfaces;

public interface IMentionService
{
    Task<ServiceResult<ActivityResponse>> AddCommentWithMentionsAsync(
        Guid invoiceId, AddCommentWithMentionsRequest request, Guid userId);
    Task<ServiceResult<List<MentionResponse>>> GetUnreadMentionsAsync(Guid userId);
    Task<ServiceResult> MarkMentionsReadAsync(Guid userId, List<Guid>? mentionIds = null);
    Task<int> GetUnreadCountAsync(Guid userId);
}

public interface ITaskService
{
    Task<ServiceResult<TaskResponse>> CreateAsync(Guid invoiceId, CreateTaskRequest request, Guid assignedByUserId);
    Task<ServiceResult<TaskResponse>> UpdateAsync(Guid taskId, UpdateTaskRequest request, Guid userId);
    Task<ServiceResult<List<TaskResponse>>> GetForInvoiceAsync(Guid invoiceId, Guid userId);
    Task<ServiceResult<List<TaskResponse>>> GetAssignedToUserAsync(Guid userId, TaskFilterRequest filter);
    Task<ServiceResult<List<TaskResponse>>> GetForCompanyAsync(Guid companyId, TaskFilterRequest filter, Guid userId);
    Task<ServiceResult> DeleteAsync(Guid taskId, Guid userId);
    Task NotifyOverdueTasksAsync();
}

public interface IDelegationService
{
    Task<ServiceResult<DelegationResponse>> CreateAsync(Guid companyId, CreateDelegationRequest request, Guid delegatorUserId);
    Task<ServiceResult<List<DelegationResponse>>> GetForUserAsync(Guid userId, Guid companyId);
    Task<ServiceResult<List<DelegationResponse>>> GetActiveForCompanyAsync(Guid companyId, Guid userId);
    Task<ServiceResult> RevokeAsync(Guid delegationId, Guid userId);
    Task<Guid> ResolveEffectiveApproverAsync(Guid originalUserId, Guid companyId, Guid? workflowTemplateId);
}
