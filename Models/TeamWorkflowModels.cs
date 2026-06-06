namespace OcrInvoiceSaaS.Models;

// ══════════════════════════════════════════════════════════════════════════════════
// @Mention Notifications
// ══════════════════════════════════════════════════════════════════════════════════

public class InvoiceMention
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceActivityId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid MentionedByUserId { get; set; }
    public Guid MentionedUserId { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }

    public InvoiceActivity Activity { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
    public User MentionedByUser { get; set; } = null!;
    public User MentionedUser { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════════
// Task Assignment
// ══════════════════════════════════════════════════════════════════════════════════

public enum TaskStatus { Open, InProgress, Completed, Cancelled }
public enum TaskPriority { Low, Medium, High, Urgent }

public class InvoiceTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AssignedToUserId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public TaskStatus Status { get; set; } = TaskStatus.Open;
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Invoice Invoice { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public User AssignedToUser { get; set; } = null!;
    public User AssignedByUser { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════════
// Approval Delegation (out-of-office cover)
// ══════════════════════════════════════════════════════════════════════════════════

public class ApprovalDelegation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid DelegatorUserId { get; set; }
    public Guid DelegateUserId { get; set; }
    public DateTime ActiveFrom { get; set; }
    public DateTime ActiveUntil { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? LimitedToWorkflowTemplateId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public User Delegator { get; set; } = null!;
    public User Delegate { get; set; } = null!;

    public bool IsCurrentlyActive =>
        IsActive &&
        DateTime.UtcNow >= ActiveFrom &&
        DateTime.UtcNow <= ActiveUntil;
}
