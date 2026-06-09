namespace OcrInvoiceSaaS.Models;

// ══════════════════════════════════════════════════════════════════════════════
// Feature 1 — Duplicate Invoice Detection
// ══════════════════════════════════════════════════════════════════════════════

public enum DuplicateStatus { Flagged, Dismissed, Confirmed }

public class InvoiceDuplicate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }

    // The newly uploaded invoice that triggered the flag
    public Guid InvoiceId { get; set; }

    // The existing invoice it duplicates
    public Guid DuplicateOfInvoiceId { get; set; }

    public string MatchReason { get; set; } = string.Empty;
    // e.g. "InvoiceNumber+Vendor", "InvoiceNumber+Amount", "Vendor+Amount+Date"

    public decimal MatchScore { get; set; }   // 0–100 confidence
    public DuplicateStatus Status { get; set; } = DuplicateStatus.Flagged;

    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
    public Invoice DuplicateOfInvoice { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 2 — Invoice Approval Workflow
// ══════════════════════════════════════════════════════════════════════════════

public enum ApprovalStepStatus { Pending, Approved, Rejected, Skipped }
public enum ApprovalInstanceStatus { Pending, InProgress, Approved, Rejected, Cancelled }

/// <summary>Reusable workflow template defined per company (e.g. "Standard Approval").</summary>
public class ApprovalWorkflowTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;           // "Standard", "High-Value"
    public string? Description { get; set; }
    public decimal? AmountThreshold { get; set; }              // auto-trigger if invoice > this
    public bool IsDefault { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public ICollection<ApprovalWorkflowStep> Steps { get; set; } = new List<ApprovalWorkflowStep>();
    public ICollection<InvoiceApprovalInstance> Instances { get; set; } = new List<InvoiceApprovalInstance>();
}

/// <summary>One step in a workflow template — defines who approves and in what order.</summary>
public class ApprovalWorkflowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowTemplateId { get; set; }
    public int StepOrder { get; set; }                         // 1 = first
    public string StepName { get; set; } = string.Empty;      // "Manager Review"
    public Guid? AssignedUserId { get; set; }                  // specific user, or null = any with role
    public string? RequiredRole { get; set; }                  // "Admin" | "Owner" | null
    public int TimeoutHours { get; set; } = 48;               // escalate after N hours
    public bool IsOptional { get; set; } = false;

    public ApprovalWorkflowTemplate WorkflowTemplate { get; set; } = null!;
    public User? AssignedUser { get; set; }
}

/// <summary>A live approval workflow attached to a specific invoice.</summary>
public class InvoiceApprovalInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid WorkflowTemplateId { get; set; }
    public int CurrentStepOrder { get; set; } = 1;
    public ApprovalInstanceStatus Status { get; set; } = ApprovalInstanceStatus.Pending;
    public Guid InitiatedByUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Invoice Invoice { get; set; } = null!;
    public ApprovalWorkflowTemplate WorkflowTemplate { get; set; } = null!;
    public User InitiatedByUser { get; set; } = null!;
    public ICollection<InvoiceApprovalAction> Actions { get; set; } = new List<InvoiceApprovalAction>();
}

/// <summary>Each individual approve / reject / comment action recorded immutably.</summary>
public class InvoiceApprovalAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApprovalInstanceId { get; set; }
    public int StepOrder { get; set; }
    public string StepName { get; set; } = string.Empty;
    public Guid ActedByUserId { get; set; }
    public ApprovalStepStatus Action { get; set; }
    public string? Comment { get; set; }
    public DateTime ActedAt { get; set; } = DateTime.UtcNow;

    public InvoiceApprovalInstance ApprovalInstance { get; set; } = null!;
    public User ActedByUser { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 3 — Bulk OCR Processing
// ══════════════════════════════════════════════════════════════════════════════

public enum BulkJobStatus { Queued, Processing, Completed, PartiallyFailed, Failed }
public enum BulkJobItemStatus { Queued, Processing, Completed, Failed }

public class BulkOcrJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public BulkJobStatus Status { get; set; } = BulkJobStatus.Queued;
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; } = 0;
    public int FailedItems { get; set; } = 0;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<BulkOcrJobItem> Items { get; set; } = new List<BulkOcrJobItem>();
}

public class BulkOcrJobItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BulkOcrJobId { get; set; }
    public Guid InvoiceId { get; set; }
    public int QueuePosition { get; set; }
    public BulkJobItemStatus Status { get; set; } = BulkJobItemStatus.Queued;
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
    public DateTime? ProcessedAt { get; set; }

    public BulkOcrJob BulkOcrJob { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 4 — Invoice ↔ Purchase Order Matching
// ══════════════════════════════════════════════════════════════════════════════

public enum PoStatus { Draft, Sent, PartiallyMatched, FullyMatched, Cancelled }
public enum PoMatchStatus { Matched, PartialMatch, Unmatched, Disputed }

public class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid? VendorId { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public DateTime PoDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "GBP";
    public PoStatus Status { get; set; } = PoStatus.Draft;
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public Vendor? Vendor { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    public ICollection<InvoicePoMatch> Matches { get; set; } = new List<InvoicePoMatch>();
}

public class PurchaseOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal? ReceivedQuantity { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;
}

public class InvoicePoMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public PoMatchStatus Status { get; set; } = PoMatchStatus.Unmatched;
    public decimal MatchScore { get; set; }                    // 0–100
    public decimal? AmountVariance { get; set; }               // invoice - PO amount
    public decimal? AmountVariancePercent { get; set; }
    public string? MatchNotes { get; set; }
    public bool IsManual { get; set; } = false;                // true = user confirmed manually
    public Guid? MatchedByUserId { get; set; }
    public DateTime MatchedAt { get; set; } = DateTime.UtcNow;

    public Invoice Invoice { get; set; } = null!;
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 5 — Currency Conversion
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Exchange rate snapshot fetched at upload time. Stored for historical accuracy.</summary>
public class ExchangeRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FromCurrency { get; set; } = string.Empty;   // e.g. "USD"
    public string ToCurrency { get; set; } = string.Empty;     // e.g. "GBP" (company base)
    public decimal Rate { get; set; }                           // 1 FROM = Rate TO
    public string Source { get; set; } = "OpenExchangeRates";  // or "Manual", "ECB"
    public DateTime RateDate { get; set; }                      // date the rate applies to
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
