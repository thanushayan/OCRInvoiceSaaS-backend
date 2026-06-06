namespace OcrInvoiceSaaS.Models;

public enum InvoiceStatus
{
    Uploaded,
    Processing,
    Processed,
    Reviewed,
    Approved,
    Failed
}

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public Guid? VendorId { get; set; }
    public Guid? ExpenseCategoryId { get; set; }

    // File metadata
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;

    // OCR-extracted fields
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? SubTotal { get; set; }
    public string? Currency { get; set; } = "GBP";
    public string? ExtractedVendorName { get; set; }
    public string? RawOcrText { get; set; }
    public string? Notes { get; set; }

    // Feature 5 — Currency conversion (stored at upload time for historical accuracy)
    public string? BaseCurrency { get; set; } = "GBP";         // company base currency
    public decimal? ExchangeRate { get; set; }                  // 1 invoice currency = N base
    public decimal? BaseCurrencyAmount { get; set; }            // TotalAmount converted to base
    public DateTime? ExchangeRateFetchedAt { get; set; }

    // Feature 1 — Duplicate flag
    public bool IsDuplicateFlagged { get; set; } = false;

    // Feature 2 — Approval workflow
    public Guid? ActiveApprovalInstanceId { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Uploaded;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Company Company { get; set; } = null!;
    public User UploadedByUser { get; set; } = null!;
    public Vendor? Vendor { get; set; }
    public ExpenseCategory? ExpenseCategory { get; set; }
    public ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();
    public ICollection<OcrProcessingLog> OcrProcessingLogs { get; set; } = new List<OcrProcessingLog>();
    public ICollection<InvoiceDuplicate> DuplicateFlags { get; set; } = new List<InvoiceDuplicate>();
    public ICollection<InvoiceApprovalInstance> ApprovalInstances { get; set; } = new List<InvoiceApprovalInstance>();
    public ICollection<InvoicePoMatch> PoMatches { get; set; } = new List<InvoicePoMatch>();
    public ICollection<BulkOcrJobItem> BulkOcrItems { get; set; } = new List<BulkOcrJobItem>();
}
