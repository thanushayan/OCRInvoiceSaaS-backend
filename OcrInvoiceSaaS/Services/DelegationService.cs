using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class DelegationService : IDelegationService
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IEmailService _email;

    public DelegationService(
        ApplicationDbContext db,
        INotificationService notifications,
        IEmailService email)
    {
        _db            = db;
        _notifications = notifications;
        _email         = email;
    }

    public async Task<ServiceResult<DelegationResponse>> CreateAsync(
        Guid companyId, CreateDelegationRequest request, Guid delegatorUserId)
    {
        // Both users must belong to the company
        var companyUserIds = await _db.CompanyUsers
            .Where(cu => cu.CompanyId == companyId)
            .Select(cu => cu.UserId)
            .ToListAsync();

        if (!companyUserIds.Contains(delegatorUserId))
            return ServiceResult<DelegationResponse>.Fail("Access denied.", 403);

        if (!companyUserIds.Contains(request.DelegateUserId))
            return ServiceResult<DelegationResponse>.Fail("Delegate user is not a member of this company.", 400);

        if (request.DelegateUserId == delegatorUserId)
            return ServiceResult<DelegationResponse>.Fail("You cannot delegate to yourself.", 400);

        if (request.ActiveFrom >= request.ActiveUntil)
            return ServiceResult<DelegationResponse>.Fail("ActiveFrom must be before ActiveUntil.", 400);

        if (request.ActiveUntil < DateTime.UtcNow)
            return ServiceResult<DelegationResponse>.Fail("ActiveUntil must be in the future.", 400);

        // Prevent overlapping active delegations for the same delegator in the same company
        bool overlaps = await _db.ApprovalDelegations.AnyAsync(d =>
            d.CompanyId == companyId &&
            d.DelegatorUserId == delegatorUserId &&
            d.IsActive &&
            d.ActiveFrom < request.ActiveUntil &&
            d.ActiveUntil > request.ActiveFrom);

        if (overlaps)
            return ServiceResult<DelegationResponse>.Fail(
                "An active delegation already overlaps with this period. Revoke it first.", 409);

        var delegation = new ApprovalDelegation
        {
            CompanyId                    = companyId,
            DelegatorUserId              = delegatorUserId,
            DelegateUserId               = request.DelegateUserId,
            ActiveFrom                   = request.ActiveFrom,
            ActiveUntil                  = request.ActiveUntil,
            Reason                       = request.Reason?.Trim(),
            LimitedToWorkflowTemplateId  = request.LimitedToWorkflowTemplateId
        };

        _db.ApprovalDelegations.Add(delegation);
        await _db.SaveChangesAsync();

        // Notify the delegate
        var delegator = await _db.Users.FindAsync(delegatorUserId);
        var delegate_ = await _db.Users.FindAsync(request.DelegateUserId);

        if (delegator != null && delegate_ != null)
        {
            await _notifications.CreateAsync(
                request.DelegateUserId,
                $"{delegator.FullName} has delegated approvals to you",
                $"You will act on their behalf from {request.ActiveFrom:dd MMM} to {request.ActiveUntil:dd MMM yyyy}.",
                "Info");

            await _email.SendAsync(
                to: delegate_.Email,
                subject: $"Approval delegation from {delegator.FullName}",
                body: $@"
                    <p>Hi {delegate_.FullName},</p>
                    <p><strong>{delegator.FullName}</strong> has delegated their approval responsibilities to you.</p>
                    <table>
                      <tr><td><strong>From:</strong></td><td>{request.ActiveFrom:dd MMMM yyyy HH:mm} UTC</td></tr>
                      <tr><td><strong>Until:</strong></td><td>{request.ActiveUntil:dd MMMM yyyy HH:mm} UTC</td></tr>
                      {(request.Reason != null ? $"<tr><td><strong>Reason:</strong></td><td>{request.Reason}</td></tr>" : "")}
                    </table>
                    <p>During this period, any approval steps assigned to {delegator.FullName} will be
                    routed to you automatically.</p>");
        }

        return ServiceResult<DelegationResponse>.Success(await BuildResponseAsync(delegation.Id), 201);
    }

    public async Task<ServiceResult<List<DelegationResponse>>> GetForUserAsync(Guid userId, Guid companyId)
    {
        bool isMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<DelegationResponse>>.Fail("Access denied.", 403);

        var ids = await _db.ApprovalDelegations
            .Where(d => d.CompanyId == companyId &&
                        (d.DelegatorUserId == userId || d.DelegateUserId == userId))
            .OrderByDescending(d => d.ActiveFrom)
            .Select(d => d.Id)
            .ToListAsync();

        var responses = new List<DelegationResponse>();
        foreach (var id in ids)
            responses.Add(await BuildResponseAsync(id));

        return ServiceResult<List<DelegationResponse>>.Success(responses);
    }

    public async Task<ServiceResult<List<DelegationResponse>>> GetActiveForCompanyAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<DelegationResponse>>.Fail("Access denied.", 403);

        var now = DateTime.UtcNow;
        var ids = await _db.ApprovalDelegations
            .Where(d => d.CompanyId == companyId &&
                        d.IsActive &&
                        d.ActiveFrom <= now &&
                        d.ActiveUntil >= now)
            .Select(d => d.Id)
            .ToListAsync();

        var responses = new List<DelegationResponse>();
        foreach (var id in ids)
            responses.Add(await BuildResponseAsync(id));

        return ServiceResult<List<DelegationResponse>>.Success(responses);
    }

    public async Task<ServiceResult> RevokeAsync(Guid delegationId, Guid userId)
    {
        var delegation = await _db.ApprovalDelegations
            .Include(d => d.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(d => d.Id == delegationId);

        if (delegation == null) return ServiceResult.Fail("Delegation not found.", 404);

        bool canRevoke = delegation.DelegatorUserId == userId ||
                         delegation.Company.CompanyUsers.Any(cu =>
                             cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));

        if (!canRevoke) return ServiceResult.Fail("Only the delegator or an admin can revoke a delegation.", 403);

        delegation.IsActive    = false;
        delegation.ActiveUntil = DateTime.UtcNow; // expire immediately
        delegation.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Notify the delegate
        var delegate_ = await _db.Users.FindAsync(delegation.DelegateUserId);
        if (delegate_ != null)
        {
            await _notifications.CreateAsync(
                delegation.DelegateUserId,
                "Approval delegation revoked",
                "Your approval delegation has been ended early.",
                "Info");
        }

        return ServiceResult.Success(204);
    }

    public async Task<Guid> ResolveEffectiveApproverAsync(
        Guid originalUserId, Guid companyId, Guid? workflowTemplateId)
    {
        var now = DateTime.UtcNow;

        var delegation = await _db.ApprovalDelegations
            .Where(d =>
                d.CompanyId       == companyId &&
                d.DelegatorUserId == originalUserId &&
                d.IsActive        &&
                d.ActiveFrom      <= now &&
                d.ActiveUntil     >= now &&
                (d.LimitedToWorkflowTemplateId == null ||
                 d.LimitedToWorkflowTemplateId == workflowTemplateId))
            .FirstOrDefaultAsync();

        return delegation?.DelegateUserId ?? originalUserId;
    }

    private async Task<DelegationResponse> BuildResponseAsync(Guid delegationId)
    {
        var d = await _db.ApprovalDelegations
            .Include(x => x.Delegator)
            .Include(x => x.Delegate)
            .Include(x => x.LimitedToWorkflowTemplateId.HasValue
                ? _db.ApprovalWorkflowTemplates.FirstOrDefault(t => t.Id == x.LimitedToWorkflowTemplateId)
                : null)
            .FirstAsync(x => x.Id == delegationId);

        string? workflowName = null;
        if (d.LimitedToWorkflowTemplateId.HasValue)
        {
            workflowName = await _db.ApprovalWorkflowTemplates
                .Where(t => t.Id == d.LimitedToWorkflowTemplateId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync();
        }

        return new DelegationResponse
        {
            Id                       = d.Id,
            DelegatorName            = d.Delegator.FullName,
            DelegatorEmail           = d.Delegator.Email,
            DelegateName             = d.Delegate.FullName,
            DelegateEmail            = d.Delegate.Email,
            ActiveFrom               = d.ActiveFrom,
            ActiveUntil              = d.ActiveUntil,
            Reason                   = d.Reason,
            LimitedToWorkflowName    = workflowName,
            IsCurrentlyActive        = d.IsCurrentlyActive,
            IsActive                 = d.IsActive,
            CreatedAt                = d.CreatedAt
        };
    }
}
