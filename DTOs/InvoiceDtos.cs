using System.ComponentModel.DataAnnotations;
using OcrInvoiceSaaS.Models;

namespace OcrInvoiceSaaS.DTOs;

public class CreateInvoiceRequest
{
    [Required, MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string FileUrl { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string FileType { get; set; } = string.Empty;

    public Guid? VendorId { get; set; }
    public Guid? ExpenseCategoryId { get; set; }
    public string? Notes { get; set; }
}

public class UpdateInvoiceRequest
{
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? SubTotal { get; set; }
    public string? Currency { get; set; }
    public Guid? VendorId { get; set; }
    public Guid? ExpenseCategoryId { get; set; }
    public string? Notes { get; set; }
    public InvoiceStatus? Status { get; set; }
}

public class InvoiceResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? SubTotal { get; set; }
    public string? Currency { get; set; }
    public string? ExtractedVendorName { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;

    // Currency conversion (Feature 4)
    public string? BaseCurrency { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal? BaseCurrencyAmount { get; set; }
    public DateTime? ExchangeRateFetchedAt { get; set; }

    // Duplicate detection / approval workflow state (Features 1 & 2)
    public bool IsDuplicateFlagged { get; set; }
    public Guid? ActiveApprovalInstanceId { get; set; }

    public VendorResponse? Vendor { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<InvoiceItemResponse> Items { get; set; } = new();
}

public class InvoiceItemResponse
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal? TaxRate { get; set; }
}

public class InvoiceListResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? VendorName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OcrResultResponse
{
    public string? RawText { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? VendorName { get; set; }
    public string Provider { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
