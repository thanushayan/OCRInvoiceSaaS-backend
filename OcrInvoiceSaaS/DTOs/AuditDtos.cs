namespace OcrInvoiceSaaS.DTOs;

public class AuditLogResponse
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ChangeSummary { get; set; }
    public Guid PerformedByUserId { get; set; }
    public string PerformedByName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
