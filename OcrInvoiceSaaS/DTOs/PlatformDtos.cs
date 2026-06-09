using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
// Reporting & Export
// ══════════════════════════════════════════════════════════════════════════════

public class ReportFilterRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Currency { get; set; }
    public Guid? VendorId { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Status { get; set; }
}

public class SpendAnalyticsResponse
{
    public decimal TotalSpend { get; set; }
    public decimal TotalTax { get; set; }
    public int TotalInvoices { get; set; }
    public string BaseCurrency { get; set; } = "GBP";
    public List<SpendByVendor> ByVendor { get; set; } = new();
    public List<SpendByCategory> ByCategory { get; set; } = new();
    public List<SpendByMonth> ByMonth { get; set; } = new();
    public List<SpendByCurrency> ByCurrency { get; set; } = new();
}

public class SpendByVendor
{
    public Guid? VendorId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal Percentage { get; set; }
}

public class SpendByCategory
{
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal Percentage { get; set; }
}

public class SpendByMonth
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthLabel { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TaxAmount { get; set; }
}

public class SpendByCurrency
{
    public string Currency { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal TotalOriginalAmount { get; set; }
    public decimal TotalBaseCurrencyAmount { get; set; }
}

public class VatSummaryResponse
{
    public string Period { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string BaseCurrency { get; set; } = "GBP";
    public decimal TotalNetAmount { get; set; }               // SubTotal sum
    public decimal TotalVatAmount { get; set; }               // TaxAmount sum
    public decimal TotalGrossAmount { get; set; }             // TotalAmount sum
    public int InvoiceCount { get; set; }
    public List<VatByRate> ByRate { get; set; } = new();
}

public class VatByRate
{
    public decimal TaxRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public int InvoiceCount { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// Webhooks
// ══════════════════════════════════════════════════════════════════════════════

public class CreateWebhookRequest
{
    [Required, Url, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    public List<string> Events { get; set; } = new();         // empty = subscribe to all
}

public class CreateWebhookResponse
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SigningSecret { get; set; } = string.Empty; // shown ONCE — use to verify HMAC signatures
    public List<string> Events { get; set; } = new();
    public string Warning { get; set; } = "Store this signing secret securely. It will not be shown again.";
    public DateTime CreatedAt { get; set; }
}

public class WebhookResponse
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SecretPrefix { get; set; } = string.Empty;
    public List<string> Events { get; set; } = new();
    public bool IsActive { get; set; }
    public int TotalDeliveries { get; set; }
    public int FailedDeliveries { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WebhookDeliveryResponse
{
    public Guid Id { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// Invoice Comments & Activity
// ══════════════════════════════════════════════════════════════════════════════

public class AddCommentRequest
{
    [Required, MaxLength(2000)]
    public string Comment { get; set; } = string.Empty;
}

public class ActivityResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string? Metadata { get; set; }
    public bool IsSystemGenerated { get; set; }
    public string? UserName { get; set; }
    public string? UserInitials { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// Rate Limiting
// ══════════════════════════════════════════════════════════════════════════════

public class RateLimitStatusResponse
{
    public int RequestsUsed { get; set; }
    public int RequestsLimit { get; set; }
    public int RequestsRemaining { get; set; }
    public double PercentUsed { get; set; }
    public DateTime WindowResetAt { get; set; }
    public bool IsThrottled { get; set; }
}
