using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class ActivityService : IActivityService
{
    private readonly ApplicationDbContext _db;
    private readonly MentionService _mentions;

    public ActivityService(ApplicationDbContext db, MentionService mentions)
    {
        _db       = db;
        _mentions = mentions;
    }

    public async Task<ServiceResult<List<ActivityResponse>>> GetForInvoiceAsync(Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<List<ActivityResponse>>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<List<ActivityResponse>>.Fail("Access denied.", 403);

        var activities = await _db.InvoiceActivities
            .Include(a => a.User)
            .Where(a => a.InvoiceId == invoiceId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ActivityResponse
            {
                Id                = a.Id,
                Type              = a.Type.ToString(),
                Comment           = a.Comment,
                Metadata          = a.Metadata,
                IsSystemGenerated = a.IsSystemGenerated,
                UserName          = a.User != null ? a.User.FullName : "System",
                UserInitials      = a.User != null ? BuildInitials(a.User.FullName) : "SY",
                CreatedAt         = a.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<ActivityResponse>>.Success(activities);
    }

    public async Task<ServiceResult<ActivityResponse>> AddCommentAsync(
        Guid invoiceId, AddCommentRequest request, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<ActivityResponse>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<ActivityResponse>.Fail("Access denied.", 403);

        var user = await _db.Users.FindAsync(userId);

        var activity = new InvoiceActivity
        {
            InvoiceId           = invoiceId,
            UserId              = userId,
            Type                = ActivityType.Comment,
            Comment             = request.Comment.Trim(),
            IsSystemGenerated   = false
        };

        _db.InvoiceActivities.Add(activity);
        await _db.SaveChangesAsync();

        await _mentions.ProcessMentionsAsync(invoiceId, invoice.Company.Id, userId, request.Comment.Trim());

        return ServiceResult<ActivityResponse>.Success(new ActivityResponse
        {
            Id                = activity.Id,
            Type              = activity.Type.ToString(),
            Comment           = activity.Comment,
            IsSystemGenerated = false,
            UserName          = user?.FullName ?? "Unknown",
            UserInitials      = BuildInitials(user?.FullName ?? "U"),
            CreatedAt         = activity.CreatedAt
        }, 201);
    }

    public async Task<ServiceResult> DeleteCommentAsync(Guid activityId, Guid userId)
    {
        var activity = await _db.InvoiceActivities
            .Include(a => a.Invoice).ThenInclude(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(a => a.Id == activityId);

        if (activity == null) return ServiceResult.Fail("Comment not found.", 404);
        if (activity.IsSystemGenerated) return ServiceResult.Fail("System-generated activities cannot be deleted.", 400);

        bool isMember = activity.Invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId);
        if (!isMember) return ServiceResult.Fail("Access denied.", 403);

        bool isOwnerOrAdmin = activity.Invoice.Company.CompanyUsers.Any(cu =>
            cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));

        if (activity.UserId != userId && !isOwnerOrAdmin)
            return ServiceResult.Fail("You can only delete your own comments.", 403);

        _db.InvoiceActivities.Remove(activity);
        await _db.SaveChangesAsync();
        return ServiceResult.Success(204);
    }

    public async Task LogSystemEventAsync(Guid invoiceId, ActivityType type, string? metadata = null)
    {
        _db.InvoiceActivities.Add(new InvoiceActivity
        {
            InvoiceId         = invoiceId,
            UserId            = null,
            Type              = type,
            Metadata          = metadata,
            IsSystemGenerated = true
        });

        await _db.SaveChangesAsync();
    }

    private static string BuildInitials(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[^1][0]}".ToUpper()
            : fullName[..Math.Min(2, fullName.Length)].ToUpper();
    }
}
