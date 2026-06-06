namespace OcrInvoiceSaaS.Models;

/// <summary>
/// Per-user notification preferences. Controls which channels each event type uses.
/// Defaults: all channels on. Users can opt out per event.
/// </summary>
public class NotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    // Each property: true = receive this notification
    public bool OcrCompletedEmail { get; set; } = true;
    public bool OcrCompletedInApp { get; set; } = true;

    public bool ApprovalRequiredEmail { get; set; } = true;
    public bool ApprovalRequiredInApp { get; set; } = true;

    public bool ApprovalCompletedEmail { get; set; } = true;
    public bool ApprovalCompletedInApp { get; set; } = true;

    public bool DuplicateFlaggedEmail { get; set; } = true;
    public bool DuplicateFlaggedInApp { get; set; } = true;

    public bool DueDateReminderEmail { get; set; } = true;
    public bool DueDateReminderInApp { get; set; } = true;

    public bool MentionEmail { get; set; } = true;
    public bool MentionInApp { get; set; } = true;

    public bool BulkOcrCompletedEmail { get; set; } = true;
    public bool BulkOcrCompletedInApp { get; set; } = true;

    public bool WeeklyDigestEmail { get; set; } = false; // opt-in

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
