using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
// Feature 1 — Duplicate Detection
// ══════════════════════════════════════════════════════════════════════════════

public class DuplicateCheckResult
{
    public bool HasDuplicates { get; set; }
    public List<DuplicateFlagResponse> Duplicates { get; set; } = new();
}

public class DuplicateFlagResponse
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid DuplicateOfInvoiceId { get; set; }
    public string DuplicateOfInvoiceNumber { get; set; } = string.Empty;
    public string DuplicateOfFileName { get; set; } = string.Empty;
    public DateTime? DuplicateOfInvoiceDate { get; set; }
    public decimal? DuplicateOfAmount { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public decimal MatchScore { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
}

public class ReviewDuplicateRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;   // "Dismissed" | "Confirmed"

    [MaxLength(500)]
    public string? Note { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 2 — Approval Workflow
// ══════════════════════════════════════════════════════════════════════════════

public class CreateWorkflowTemplateRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public decimal? AmountThreshold { get; set; }
    public bool IsDefault { get; set; } = false;

    [Required, MinLength(1)]
    public List<WorkflowStepRequest> Steps { get; set; } = new();
}

public class WorkflowStepRequest
{
    [Required, MaxLength(100)]
    public string StepName { get; set; } = string.Empty;

    [Range(1, 20)]
    public int StepOrder { get; set; }

    public Guid? AssignedUserId { get; set; }
    public string? RequiredRole { get; set; }

    [Range(1, 720)]
    public int TimeoutHours { get; set; } = 48;

    public bool IsOptional { get; set; } = false;
}

public class WorkflowTemplateResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? AmountThreshold { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public List<WorkflowStepResponse> Steps { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class WorkflowStepResponse
{
    public Guid Id { get; set; }
    public int StepOrder { get; set; }
    public string StepName { get; set; } = string.Empty;
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public string? RequiredRole { get; set; }
    public int TimeoutHours { get; set; }
    public bool IsOptional { get; set; }
}

public class StartApprovalRequest
{
    [Required]
    public Guid WorkflowTemplateId { get; set; }
}

public class ApprovalInstanceResponse
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string WorkflowName { get; set; } = string.Empty;
    public int CurrentStepOrder { get; set; }
    public string CurrentStepName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string InitiatedByName { get; set; } = string.Empty;
    public List<ApprovalActionResponse> Actions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class ApprovalActionResponse
{
    public Guid Id { get; set; }
    public int StepOrder { get; set; }
    public string StepName { get; set; } = string.Empty;
    public string ActedByName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime ActedAt { get; set; }
}

public class SubmitApprovalActionRequest
{
    [Required]
    public string Action { get; set; } = string.Empty;    // "Approved" | "Rejected"

    [MaxLength(1000)]
    public string? Comment { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 3 — Bulk OCR Processing
// ══════════════════════════════════════════════════════════════════════════════

public class CreateBulkOcrJobRequest
{
    [Required, MinLength(1)]
    public List<Guid> InvoiceIds { get; set; } = new();
}

public class BulkOcrJobResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int FailedItems { get; set; }
    public int PendingItems { get; set; }
    public double ProgressPercent { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<BulkOcrJobItemResponse> Items { get; set; } = new();
}

public class BulkOcrJobItemResponse
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int QueuePosition { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 4 — Purchase Order Matching
// ══════════════════════════════════════════════════════════════════════════════

public class CreatePurchaseOrderRequest
{
    [Required, MaxLength(100)]
    public string PoNumber { get; set; } = string.Empty;

    [Required]
    public DateTime PoDate { get; set; }

    public DateTime? ExpectedDeliveryDate { get; set; }

    public Guid? VendorId { get; set; }

    [MaxLength(10)]
    public string Currency { get; set; } = "GBP";

    public string? Notes { get; set; }

    [Required, MinLength(1)]
    public List<CreatePoItemRequest> Items { get; set; } = new();
}

public class CreatePoItemRequest
{
    [Required, MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(0.0001, double.MaxValue)]
    public decimal Quantity { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal UnitPrice { get; set; }
}

public class PurchaseOrderResponse
{
    public Guid Id { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public DateTime PoDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? VendorName { get; set; }
    public string? Notes { get; set; }
    public List<PoItemResponse> Items { get; set; } = new();
    public List<PoMatchSummary> Matches { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class PoItemResponse
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal? ReceivedQuantity { get; set; }
}

public class PoMatchSummary
{
    public Guid MatchId { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal? InvoiceAmount { get; set; }
    public decimal MatchScore { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? AmountVariance { get; set; }
    public decimal? AmountVariancePercent { get; set; }
}

public class MatchInvoiceToPoRequest
{
    [Required]
    public Guid PurchaseOrderId { get; set; }
}

public class PoMatchResponse
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public decimal MatchScore { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? AmountVariance { get; set; }
    public decimal? AmountVariancePercent { get; set; }
    public string? MatchNotes { get; set; }
    public bool IsManual { get; set; }
    public DateTime MatchedAt { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// Feature 5 — Currency Conversion
// ══════════════════════════════════════════════════════════════════════════════

public class CurrencyConversionResult
{
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public decimal ConvertedAmount { get; set; }
    public decimal Rate { get; set; }
    public DateTime RateDate { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class ExchangeRateResponse
{
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime RateDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime FetchedAt { get; set; }
}

public class MultiCurrencyInvoiceRequest
{
    [Required, MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string FileUrl { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string FileType { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string Currency { get; set; } = "GBP";

    public Guid? VendorId { get; set; }
    public Guid? ExpenseCategoryId { get; set; }
    public string? Notes { get; set; }

    // Optional — override automatic rate lookup
    public decimal? ManualExchangeRate { get; set; }
}
