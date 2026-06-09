namespace OcrInvoiceSaaS.Models;

// ══════════════════════════════════════════════════════════════════════════════
// Webhooks
// ══════════════════════════════════════════════════════════════════════════════

public enum WebhookEvent
{
    InvoiceCreated,
    InvoiceOcrCompleted,
    InvoiceStatusChanged,
    InvoiceApproved,
    InvoiceRejected,
    DuplicateFlagged,
    BulkOcrCompleted,
    PaymentRecorded,
    ApprovalRequested
}

public enum WebhookDeliveryStatus { Pending, Succeeded, Failed, Retrying }

public class WebhookEndpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;   // HMAC-SHA256 signing secret (hashed for storage)
    public string SecretPrefix { get; set; } = string.Empty; // first 4 chars shown in UI
    public string[] Events { get; set; } = [];               // subscribed event names; empty = all
    public bool IsActive { get; set; } = true;
    public DateTime? LastTriggeredAt { get; set; }
    public int TotalDeliveries { get; set; } = 0;
    public int FailedDeliveries { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();
}

public class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WebhookEndpointId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;      // JSON body sent
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int AttemptCount { get; set; } = 0;
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public WebhookEndpoint WebhookEndpoint { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Invoice Comments & Activity Feed
// ══════════════════════════════════════════════════════════════════════════════

public enum ActivityType
{
    Comment,
    StatusChanged,
    OcrCompleted,
    ApprovalStarted,
    ApprovalActioned,
    DuplicateFlagged,
    PoMatched,
    FieldUpdated
}

public class InvoiceActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid? UserId { get; set; }                         // null = system
    public ActivityType Type { get; set; }
    public string? Comment { get; set; }
    public string? Metadata { get; set; }                     // JSON for structured data
    public bool IsSystemGenerated { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Invoice Invoice { get; set; } = null!;
    public User? User { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// Rate Limiting
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Tracks API request counts per company per window for subscription-based throttling.</summary>
public class ApiUsageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string WindowKey { get; set; } = string.Empty;    // e.g. "2026-04-05:invoices"
    public int RequestCount { get; set; } = 0;
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Due Date Reminder Tracking
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Prevents duplicate reminder emails — one record per invoice per reminder threshold.</summary>
public class DueReminderSent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public int DaysBeforeDue { get; set; }                    // e.g. 7, 3, 1
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public Invoice Invoice { get; set; } = null!;
}
