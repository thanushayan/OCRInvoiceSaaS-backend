using AutoMapper;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public SubscriptionService(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }

    public async Task<ServiceResult<List<SubscriptionPlanResponse>>> GetPlansAsync()
    {
        var plans = await _db.SubscriptionPlans.Where(p => p.IsActive).OrderBy(p => p.MonthlyPrice).ToListAsync();
        return ServiceResult<List<SubscriptionPlanResponse>>.Success(_mapper.Map<List<SubscriptionPlanResponse>>(plans));
    }

    public async Task<ServiceResult<CompanySubscriptionResponse>> SubscribeAsync(
        Guid companyId, CreateCompanySubscriptionRequest request, Guid userId)
    {
        bool isOwnerOrAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isOwnerOrAdmin) return ServiceResult<CompanySubscriptionResponse>.Fail("Only company owners or admins can manage subscriptions.", 403);

        var plan = await _db.SubscriptionPlans.FindAsync(request.SubscriptionPlanId);
        if (plan == null) return ServiceResult<CompanySubscriptionResponse>.Fail("Subscription plan not found.", 404);

        var existing = await _db.CompanySubscriptions.Where(cs => cs.CompanyId == companyId && cs.IsActive).ToListAsync();
        foreach (var sub in existing) { sub.IsActive = false; sub.Status = "Superseded"; sub.EndDate = DateTime.UtcNow; sub.UpdatedAt = DateTime.UtcNow; }

        var newSub = new CompanySubscription { CompanyId = companyId, SubscriptionPlanId = request.SubscriptionPlanId, StartDate = request.StartDate, IsActive = true, Status = "Active" };
        _db.CompanySubscriptions.Add(newSub);
        await _db.SaveChangesAsync();

        var created = await _db.CompanySubscriptions.Include(cs => cs.SubscriptionPlan).FirstAsync(cs => cs.Id == newSub.Id);
        return ServiceResult<CompanySubscriptionResponse>.Success(_mapper.Map<CompanySubscriptionResponse>(created), 201);
    }

    public async Task<ServiceResult<CompanySubscriptionResponse>> GetActiveSubscriptionAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<CompanySubscriptionResponse>.Fail("Access denied.", 403);

        var sub = await _db.CompanySubscriptions.Include(cs => cs.SubscriptionPlan).FirstOrDefaultAsync(cs => cs.CompanyId == companyId && cs.IsActive);
        if (sub == null) return ServiceResult<CompanySubscriptionResponse>.Fail("No active subscription found.", 404);
        return ServiceResult<CompanySubscriptionResponse>.Success(_mapper.Map<CompanySubscriptionResponse>(sub));
    }

    public async Task<ServiceResult<PaymentResponse>> RecordPaymentAsync(Guid subscriptionId, RecordPaymentRequest request, Guid userId)
    {
        var sub = await _db.CompanySubscriptions
            .Include(cs => cs.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(cs => cs.Id == subscriptionId);
        if (sub == null) return ServiceResult<PaymentResponse>.Fail("Subscription not found.", 404);

        bool isOwnerOrAdmin = sub.Company.CompanyUsers.Any(cu => cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isOwnerOrAdmin) return ServiceResult<PaymentResponse>.Fail("Only owners or admins can record payments.", 403);

        var payment = new Payment
        {
            CompanySubscriptionId = subscriptionId, Amount = request.Amount, Currency = request.Currency.ToUpper(),
            Status = "Completed", PaymentReference = request.PaymentReference?.Trim(),
            Provider = request.Provider?.Trim(), PaidAt = request.PaidAt
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return ServiceResult<PaymentResponse>.Success(_mapper.Map<PaymentResponse>(payment), 201);
    }
}
