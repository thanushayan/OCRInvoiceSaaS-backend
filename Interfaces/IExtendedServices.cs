using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Interfaces;

public interface INotificationService
{
    Task<ServiceResult<List<NotificationResponse>>> GetForUserAsync(Guid userId, bool unreadOnly = false);
    Task<ServiceResult> MarkReadAsync(Guid userId, MarkNotificationsReadRequest request);
    Task CreateAsync(Guid userId, string title, string message, string type = "Info");
}

public interface IExpenseCategoryService
{
    Task<ServiceResult<ExpenseCategoryResponse>> CreateAsync(Guid companyId, CreateExpenseCategoryRequest request, Guid userId);
    Task<ServiceResult<List<ExpenseCategoryResponse>>> GetByCompanyAsync(Guid companyId, Guid userId);
    Task<ServiceResult> DeleteAsync(Guid categoryId, Guid userId);
}

public interface IInvoiceItemService
{
    Task<ServiceResult<InvoiceItemResponse>> AddItemAsync(Guid invoiceId, AddInvoiceItemRequest request, Guid userId);
    Task<ServiceResult<InvoiceItemResponse>> UpdateItemAsync(Guid itemId, UpdateInvoiceItemRequest request, Guid userId);
    Task<ServiceResult> DeleteItemAsync(Guid itemId, Guid userId);
}

public interface ISubscriptionService
{
    Task<ServiceResult<List<SubscriptionPlanResponse>>> GetPlansAsync();
    Task<ServiceResult<CompanySubscriptionResponse>> SubscribeAsync(Guid companyId, CreateCompanySubscriptionRequest request, Guid userId);
    Task<ServiceResult<CompanySubscriptionResponse>> GetActiveSubscriptionAsync(Guid companyId, Guid userId);
    Task<ServiceResult<PaymentResponse>> RecordPaymentAsync(Guid subscriptionId, RecordPaymentRequest request, Guid userId);
}

public interface ICompanyMemberService
{
    Task<ServiceResult<List<CompanyMemberResponse>>> GetMembersAsync(Guid companyId, Guid userId);
    Task<ServiceResult<CompanyMemberResponse>> AddMemberAsync(Guid companyId, InviteMemberRequest request, Guid requestingUserId);
    Task<ServiceResult> RemoveMemberAsync(Guid companyId, Guid targetUserId, Guid requestingUserId);
}
