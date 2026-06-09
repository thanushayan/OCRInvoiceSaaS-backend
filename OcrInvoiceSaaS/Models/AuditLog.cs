namespace OcrInvoiceSaaS.Models;

/// <summary>
/// Extended immutable audit log entry for SOC 2 / ISO 27001 compliance.
/// Written once, never updated or deleted within the retention period.
/// Includes HTTP context, outcome, and a tamper-evident hash chain.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── What happened ─────────────────────────────────────────────────────────
    public string EntityType { get; set; } = string.Empty;   // "Invoice", "User", "Company"
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;       // "Created", "Approved", "Locked"
    public string? ChangeSummary { get; set; }               // JSON diff or plain summary
    public string Outcome { get; set; } = "Success";         // Success | Failure | Partial

    // ── Who did it ────────────────────────────────────────────────────────────
    public Guid? PerformedByUserId { get; set; }
    public string? PerformedByEmail { get; set; }            // denormalised — survives user deletion
    public string? AuthMethod { get; set; }                  // "jwt" | "apikey" | "system"

    // ── HTTP context (SOC 2 CC6.1 — logical access) ───────────────────────────
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? HttpMethod { get; set; }                  // GET, POST, PATCH …
    public string? HttpPath { get; set; }                    // /api/invoices/{id}/ocr
    public int? HttpStatusCode { get; set; }
    public string? RequestId { get; set; }                   // correlation ID from X-Request-Id header
    public string? SessionId { get; set; }                   // JWT jti claim

    // ── Tamper-evident chain (ISO 27001 A.12.4.2) ────────────────────────────
    /// <summary>SHA-256 of (PreviousHash + Id + EntityType + Action + Timestamp)</summary>
    public string? EntryHash { get; set; }
    /// <summary>Hash of the immediately preceding AuditLog row — links entries into a chain.</summary>
    public string? PreviousHash { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation — no cascade delete; logs are permanent
    public User? PerformedByUser { get; set; }
}

/// <summary>
/// High-priority security events classified for SOC 2 CC7 (system operations)
/// and ISO 27001 A.12.4.1 (event logging).
/// Separate from the general AuditLog so security teams can query efficiently.
/// </summary>
public enum SecurityEventSeverity { Info, Low, Medium, High, Critical }
public enum SecurityEventCategory
{
    Authentication,     // login, logout, token refresh
    Authorization,      // access denied, privilege escalation attempt
    DataAccess,         // bulk export, sensitive record read
    DataMutation,       // invoice locked/unlocked, user deleted
    Configuration,      // IP allowlist changed, webhook added
    Compliance,         // GDPR erasure, data retention enforced
    Integration,        // Xero connected, Stripe webhook received
    AnomalyDetected     // unusual login location, excessive failed attempts
}

public class SecurityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SecurityEventCategory Category { get; set; }
    public SecurityEventSeverity Severity { get; set; }
    public string EventCode { get; set; } = string.Empty;    // e.g. "AUTH_LOGIN_FAILED", "IP_BLOCKED"
    public string Description { get; set; } = string.Empty;
    public string? Detail { get; set; }                      // JSON payload with full context
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public Guid? CompanyId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestId { get; set; }
    public bool RequiresReview { get; set; } = false;        // flags for security team attention
    public bool IsReviewed { get; set; } = false;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
