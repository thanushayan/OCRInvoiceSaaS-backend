using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

// ── Notifications ─────────────────────────────────────────────

public class NotificationResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MarkNotificationsReadRequest
{
    public List<Guid>? Ids { get; set; } // null = mark all read
}

// ── ExpenseCategory ───────────────────────────────────────────

public class CreateExpenseCategoryRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }
}

public class ExpenseCategoryResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
