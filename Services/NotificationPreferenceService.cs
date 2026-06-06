using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class NotificationPreferenceResponse
{
    public bool OcrCompletedEmail { get; set; }
    public bool OcrCompletedInApp { get; set; }
    public bool ApprovalRequiredEmail { get; set; }
    public bool ApprovalRequiredInApp { get; set; }
    public bool ApprovalCompletedEmail { get; set; }
    public bool ApprovalCompletedInApp { get; set; }
    public bool DuplicateFlaggedEmail { get; set; }
    public bool DuplicateFlaggedInApp { get; set; }
    public bool DueDateReminderEmail { get; set; }
    public bool DueDateReminderInApp { get; set; }
    public bool MentionEmail { get; set; }
    public bool MentionInApp { get; set; }
    public bool BulkOcrCompletedEmail { get; set; }
    public bool BulkOcrCompletedInApp { get; set; }
    public bool WeeklyDigestEmail { get; set; }
}

public class UpdateNotificationPreferenceRequest
{
    public bool? OcrCompletedEmail { get; set; }
    public bool? OcrCompletedInApp { get; set; }
    public bool? ApprovalRequiredEmail { get; set; }
    public bool? ApprovalRequiredInApp { get; set; }
    public bool? ApprovalCompletedEmail { get; set; }
    public bool? ApprovalCompletedInApp { get; set; }
    public bool? DuplicateFlaggedEmail { get; set; }
    public bool? DuplicateFlaggedInApp { get; set; }
    public bool? DueDateReminderEmail { get; set; }
    public bool? DueDateReminderInApp { get; set; }
    public bool? MentionEmail { get; set; }
    public bool? MentionInApp { get; set; }
    public bool? BulkOcrCompletedEmail { get; set; }
    public bool? BulkOcrCompletedInApp { get; set; }
    public bool? WeeklyDigestEmail { get; set; }
}

public class NotificationPreferenceService
{
    private readonly ApplicationDbContext _db;
    public NotificationPreferenceService(ApplicationDbContext db) => _db = db;

    public async Task<ServiceResult<NotificationPreferenceResponse>> GetAsync(Guid userId)
        => ServiceResult<NotificationPreferenceResponse>.Success(Map(await GetOrCreateAsync(userId)));

    public async Task<ServiceResult<NotificationPreferenceResponse>> UpdateAsync(Guid userId, UpdateNotificationPreferenceRequest request)
    {
        var prefs = await GetOrCreateAsync(userId);
        if (request.OcrCompletedEmail.HasValue) prefs.OcrCompletedEmail = request.OcrCompletedEmail.Value;
        if (request.OcrCompletedInApp.HasValue) prefs.OcrCompletedInApp = request.OcrCompletedInApp.Value;
        if (request.ApprovalRequiredEmail.HasValue) prefs.ApprovalRequiredEmail = request.ApprovalRequiredEmail.Value;
        if (request.ApprovalRequiredInApp.HasValue) prefs.ApprovalRequiredInApp = request.ApprovalRequiredInApp.Value;
        if (request.ApprovalCompletedEmail.HasValue) prefs.ApprovalCompletedEmail = request.ApprovalCompletedEmail.Value;
        if (request.ApprovalCompletedInApp.HasValue) prefs.ApprovalCompletedInApp = request.ApprovalCompletedInApp.Value;
        if (request.DuplicateFlaggedEmail.HasValue) prefs.DuplicateFlaggedEmail = request.DuplicateFlaggedEmail.Value;
        if (request.DuplicateFlaggedInApp.HasValue) prefs.DuplicateFlaggedInApp = request.DuplicateFlaggedInApp.Value;
        if (request.DueDateReminderEmail.HasValue) prefs.DueDateReminderEmail = request.DueDateReminderEmail.Value;
        if (request.DueDateReminderInApp.HasValue) prefs.DueDateReminderInApp = request.DueDateReminderInApp.Value;
        if (request.MentionEmail.HasValue) prefs.MentionEmail = request.MentionEmail.Value;
        if (request.MentionInApp.HasValue) prefs.MentionInApp = request.MentionInApp.Value;
        if (request.BulkOcrCompletedEmail.HasValue) prefs.BulkOcrCompletedEmail = request.BulkOcrCompletedEmail.Value;
        if (request.BulkOcrCompletedInApp.HasValue) prefs.BulkOcrCompletedInApp = request.BulkOcrCompletedInApp.Value;
        if (request.WeeklyDigestEmail.HasValue) prefs.WeeklyDigestEmail = request.WeeklyDigestEmail.Value;
        prefs.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ServiceResult<NotificationPreferenceResponse>.Success(Map(prefs));
    }

    private async Task<NotificationPreference> GetOrCreateAsync(Guid userId)
    {
        var prefs = await _db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (prefs != null) return prefs;
        prefs = new NotificationPreference { UserId = userId };
        _db.NotificationPreferences.Add(prefs);
        await _db.SaveChangesAsync();
        return prefs;
    }

    private static NotificationPreferenceResponse Map(NotificationPreference p) => new()
    {
        OcrCompletedEmail = p.OcrCompletedEmail, OcrCompletedInApp = p.OcrCompletedInApp,
        ApprovalRequiredEmail = p.ApprovalRequiredEmail, ApprovalRequiredInApp = p.ApprovalRequiredInApp,
        ApprovalCompletedEmail = p.ApprovalCompletedEmail, ApprovalCompletedInApp = p.ApprovalCompletedInApp,
        DuplicateFlaggedEmail = p.DuplicateFlaggedEmail, DuplicateFlaggedInApp = p.DuplicateFlaggedInApp,
        DueDateReminderEmail = p.DueDateReminderEmail, DueDateReminderInApp = p.DueDateReminderInApp,
        MentionEmail = p.MentionEmail, MentionInApp = p.MentionInApp,
        BulkOcrCompletedEmail = p.BulkOcrCompletedEmail, BulkOcrCompletedInApp = p.BulkOcrCompletedInApp,
        WeeklyDigestEmail = p.WeeklyDigestEmail
    };
}
