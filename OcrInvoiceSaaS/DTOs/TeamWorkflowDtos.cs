using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
// @Mention
// ══════════════════════════════════════════════════════════════════════════════

public class AddCommentWithMentionsRequest
{
    [Required, MaxLength(2000)]
    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// User IDs to @mention. The service also auto-parses @{userId} tokens
    /// embedded in the comment text for rich-text clients.
    /// </summary>
    public List<Guid> MentionedUserIds { get; set; } = new();
}

public class MentionResponse
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceFileName { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public Guid InvoiceActivityId { get; set; }
    public string CommentText { get; set; } = string.Empty;
    public string MentionedByName { get; set; } = string.Empty;
    public string MentionedByInitials { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// Task Assignment
// ══════════════════════════════════════════════════════════════════════════════

public class CreateTaskRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    public Guid AssignedToUserId { get; set; }

    public string Priority { get; set; } = "Medium"; // Low | Medium | High | Urgent

    public DateTime? DueDate { get; set; }
}

public class UpdateTaskRequest
{
    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    public Guid? AssignedToUserId { get; set; }

    public string? Priority { get; set; }

    public string? Status { get; set; } // Open | InProgress | Completed | Cancelled

    public DateTime? DueDate { get; set; }

    [MaxLength(500)]
    public string? CompletionNote { get; set; }
}

public class TaskResponse
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceFileName { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public bool IsOverdue { get; set; }
    public string AssignedToName { get; set; } = string.Empty;
    public string AssignedToInitials { get; set; } = string.Empty;
    public Guid AssignedToUserId { get; set; }
    public string AssignedByName { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TaskFilterRequest
{
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public bool OverdueOnly { get; set; } = false;
}

// ══════════════════════════════════════════════════════════════════════════════
// Approval Delegation
// ══════════════════════════════════════════════════════════════════════════════

public class CreateDelegationRequest
{
    [Required]
    public Guid DelegateUserId { get; set; }

    [Required]
    public DateTime ActiveFrom { get; set; }

    [Required]
    public DateTime ActiveUntil { get; set; }

    [MaxLength(200)]
    public string? Reason { get; set; }

    // Optionally limit delegation to a specific workflow template
    public Guid? LimitedToWorkflowTemplateId { get; set; }
}

public class DelegationResponse
{
    public Guid Id { get; set; }
    public string DelegatorName { get; set; } = string.Empty;
    public string DelegatorEmail { get; set; } = string.Empty;
    public string DelegateName { get; set; } = string.Empty;
    public string DelegateEmail { get; set; } = string.Empty;
    public DateTime ActiveFrom { get; set; }
    public DateTime ActiveUntil { get; set; }
    public string? Reason { get; set; }
    public string? LimitedToWorkflowName { get; set; }
    public bool IsCurrentlyActive { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
