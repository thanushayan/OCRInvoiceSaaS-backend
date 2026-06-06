namespace OcrInvoiceSaaS.Models;

public enum DuplicateStatus { Flagged, Dismissed, Confirmed }

public class InvoiceDuplicate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid DuplicateOfInvoiceId { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public decimal MatchScore { get; set; }
    public DuplicateStatus Status { get; set; } = DuplicateStatus.Flagged;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public Company Company { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
    public Invoice DuplicateOfInvoice { get; set; } = null!;
}

public enum ApprovalStepStatus { Pending, Approved, Rejected, Skipped }
public enum ApprovalInstanceStatus { Pending, InProgress, Approved, Rejected, Cancelled }

public class ApprovalWorkflowTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? AmountThreshold { get; set; }
    public bool IsDefault { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Company Company { get; set; } = null!;
    public ICollection<ApprovalWorkflowStep> Steps { get; set; } = new List<ApprovalWorkflowStep>();
    public ICollection<InvoiceApprovalInstance> Instances { get; set; } = new List<InvoiceApprovalInstance>();
}

public class ApprovalWorkflowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowTemplateId { get; set; }
    public int StepOrder { get; set; }
    public string StepName { get; set; } = string.Empty;
    public Guid? AssignedUserId { get; set; }
    public string? RequiredRole { get; set; }
    public int TimeoutHours { get; set; } = 48;
    public bool IsOptional { get; set; } = false;
    public ApprovalWorkflowTemplate WorkflowTemplate { get; set; } = null!;
    public User? AssignedUser { get; set; }
}

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
    public decimal MatchScore { get; set; }
    public decimal? AmountVariance { get; set; }
    public decimal? AmountVariancePercent { get; set; }
    public string? MatchNotes { get; set; }
    public bool IsManual { get; set; } = false;
    public Guid? MatchedByUserId { get; set; }
    public DateTime MatchedAt { get; set; } = DateTime.UtcNow;
    public Invoice Invoice { get; set; } = null!;
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
}

public class ExchangeRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public string Source { get; set; } = "OpenExchangeRates";
    public DateTime RateDate { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
