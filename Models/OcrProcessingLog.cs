namespace OcrInvoiceSaaS.Models;

public class OcrProcessingLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawResponse { get; set; }
    public int DurationMs { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    public Invoice Invoice { get; set; } = null!;
}
