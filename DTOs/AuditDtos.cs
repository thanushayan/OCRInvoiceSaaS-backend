namespace OcrInvoiceSaaS.DTOs;

public class AuditLogResponse
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ChangeSummary { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? PerformedByEmail { get; set; }
    public string? AuthMethod { get; set; }
    public string? IpAddress { get; set; }
    public string? HttpMethod { get; set; }
    public string? HttpPath { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? RequestId { get; set; }
    public DateTime Timestamp { get; set; }
}
