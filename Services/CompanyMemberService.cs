using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class CompanyMemberService : ICompanyMemberService
{
    private readonly ApplicationDbContext _db;

    public CompanyMemberService(ApplicationDbContext db) { _db = db; }

    public async Task<ServiceResult<List<CompanyMemberResponse>>> GetMembersAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<CompanyMemberResponse>>.Fail("Access denied.", 403);

        var members = await _db.CompanyUsers
            .Include(cu => cu.User)
            .Where(cu => cu.CompanyId == companyId)
            .OrderBy(cu => cu.User.FullName)
            .Select(cu => new CompanyMemberResponse
            {
                UserId = cu.UserId,
                FullName = cu.User.FullName,
                Email = cu.User.Email,
                Role = cu.Role,
                JoinedAt = cu.JoinedAt
            })
            .ToListAsync();

        return ServiceResult<List<CompanyMemberResponse>>.Success(members);
    }

    public async Task<ServiceResult<CompanyMemberResponse>> AddMemberAsync(
        Guid companyId, InviteMemberRequest request, Guid requestingUserId)
    {
        bool isOwnerOrAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == requestingUserId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isOwnerOrAdmin) return ServiceResult<CompanyMemberResponse>.Fail("Only owners or admins can add members.", 403);

        bool requestingUserIsOwner = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == requestingUserId && cu.Role == "Owner");
        if (request.Role == "Owner" && !requestingUserIsOwner)
            return ServiceResult<CompanyMemberResponse>.Fail("Only owners can assign the Owner role.", 403);

        var targetUser = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower() && u.IsActive);
        if (targetUser == null)
            return ServiceResult<CompanyMemberResponse>.Fail("No active account found with that email address.", 404);

        bool alreadyMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == targetUser.Id);
        if (alreadyMember)
            return ServiceResult<CompanyMemberResponse>.Fail("User is already a member of this company.", 409);

        var companyUser = new CompanyUser { CompanyId = companyId, UserId = targetUser.Id, Role = request.Role };
        _db.CompanyUsers.Add(companyUser);
        await _db.SaveChangesAsync();

        return ServiceResult<CompanyMemberResponse>.Success(new CompanyMemberResponse
        {
            UserId = targetUser.Id,
            FullName = targetUser.FullName,
            Email = targetUser.Email,
            Role = companyUser.Role,
            JoinedAt = companyUser.JoinedAt
        }, 201);
    }

    public async Task<ServiceResult> RemoveMemberAsync(Guid companyId, Guid targetUserId, Guid requestingUserId)
    {
        bool isOwnerOrAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == requestingUserId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isOwnerOrAdmin) return ServiceResult.Fail("Only owners or admins can remove members.", 403);
        if (targetUserId == requestingUserId) return ServiceResult.Fail("You cannot remove yourself from the company.", 400);

        var target = await _db.CompanyUsers
            .FirstOrDefaultAsync(cu => cu.CompanyId == companyId && cu.UserId == targetUserId);
        if (target == null) return ServiceResult.Fail("Member not found.", 404);

        if (target.Role == "Owner")
        {
            bool requestingIsOwner = await _db.CompanyUsers.AnyAsync(cu =>
                cu.CompanyId == companyId && cu.UserId == requestingUserId && cu.Role == "Owner");
            if (!requestingIsOwner) return ServiceResult.Fail("Admins cannot remove owners.", 403);
        }

        _db.CompanyUsers.Remove(target);
        await _db.SaveChangesAsync();
        return ServiceResult.Success(204);
    }
}
