namespace OcrInvoiceSaaS.Models;

// ══════════════════════════════════════════════════════════════════════════════
// Xero / QuickBooks Integration
// ══════════════════════════════════════════════════════════════════════════════

public enum AccountingProvider { Xero, QuickBooks }
public enum SyncStatus { Pending, Synced, Failed, Skipped }

public class AccountingConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public AccountingProvider Provider { get; set; }
    public string TenantId { get; set; } = string.Empty;      // Xero org / QBO realm
    public string TenantName { get; set; } = string.Empty;
    public string AccessTokenEncrypted { get; set; } = string.Empty;
    public string RefreshTokenEncrypted { get; set; } = string.Empty;
    public DateTime TokenExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncError { get; set; }

    public Company Company { get; set; } = null!;
    public ICollection<InvoiceSyncRecord> SyncRecords { get; set; } = new List<InvoiceSyncRecord>();
}

public class InvoiceSyncRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid AccountingConnectionId { get; set; }
    public string ExternalId { get; set; } = string.Empty;    // Xero/QBO invoice ID
    public string ExternalNumber { get; set; } = string.Empty;
    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdatedAt { get; set; }

    public Invoice Invoice { get; set; } = null!;
    public AccountingConnection AccountingConnection { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Stripe Integration
// ══════════════════════════════════════════════════════════════════════════════

public class StripeWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string StripeEventId { get; set; } = string.Empty;  // evt_xxx — idempotency key
    public string EventType { get; set; } = string.Empty;      // payment_intent.succeeded etc.
    public string Payload { get; set; } = string.Empty;
    public bool Processed { get; set; } = false;
    public string? ProcessingError { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    public Company Company { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// IP Allowlisting
// ══════════════════════════════════════════════════════════════════════════════

public class IpAllowlistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string CidrRange { get; set; } = string.Empty;      // e.g. "203.0.113.0/24" or "203.0.113.5/32"
    public string? Description { get; set; }                   // "London Office", "AWS Lambda"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }

    public Company Company { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Data Retention & GDPR
// ══════════════════════════════════════════════════════════════════════════════

public class DataRetentionPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public int InvoiceRetentionYears { get; set; } = 7;        // UK legal minimum
    public int AuditLogRetentionYears { get; set; } = 7;
    public int LoginAttemptRetentionDays { get; set; } = 90;
    public int NotificationRetentionDays { get; set; } = 90;
    public bool AutoDeleteEnabled { get; set; } = false;       // off by default — confirm before enabling
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid UpdatedByUserId { get; set; }

    public Company Company { get; set; } = null!;
}

public enum ErasureRequestStatus { Pending, Processing, Completed, Rejected }

public class GdprErasureRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestedByUserId { get; set; }
    public Guid? CompanyId { get; set; }
    public ErasureRequestStatus Status { get; set; } = ErasureRequestStatus.Pending;
    public string Reason { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public Guid? ProcessedByUserId { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? AuditSummary { get; set; }                  // JSON list of what was erased
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public User RequestedByUser { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Idempotency
// ══════════════════════════════════════════════════════════════════════════════

public class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;           // client-supplied Idempotency-Key header
    public Guid UserId { get; set; }
    public string RequestPath { get; set; } = string.Empty;
    public string RequestMethod { get; set; } = string.Empty;
    public int ResponseStatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
}

// ══════════════════════════════════════════════════════════════════════════════
// Invoice Locking (immutability after approval)
// ══════════════════════════════════════════════════════════════════════════════

public class InvoiceLock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid LockedByUserId { get; set; }
    public string Reason { get; set; } = "Approved — locked for audit integrity.";
    public DateTime LockedAt { get; set; } = DateTime.UtcNow;

    public Invoice Invoice { get; set; } = null!;
    public User LockedByUser { get; set; } = null!;
}
