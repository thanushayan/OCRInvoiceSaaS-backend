using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

public class CompaniesHouseLookupResponse
{
    public string CompanyNumber { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyStatus { get; set; }
    public string? CompanyType { get; set; }
    public string? RegisteredAddress { get; set; }
    public string? IncorporationDate { get; set; }
    public string? Jurisdiction { get; set; }
    public bool IsActive { get; set; }
    public string? SicCodes { get; set; }
}

public class VatValidationResponse
{
    public string VatNumber { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string? BusinessName { get; set; }
    public string? BusinessAddress { get; set; }
    public string? ConsultationNumber { get; set; }
    public DateTime CheckedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AccountingConnectionResponse
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncError { get; set; }
}

public class XeroOAuthCallbackRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;
    [Required]
    public string State { get; set; } = string.Empty;
}

public class SyncInvoiceRequest
{
    public List<Guid>? InvoiceIds { get; set; }
}

public class SyncResultResponse
{
    public int Queued { get; set; }
    public int Synced { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<SyncItemResult> Items { get; set; } = new();
}

public class SyncItemResult
{
    public Guid InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? Error { get; set; }
}

public class StripeWebhookPayload
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public class CreateIpAllowlistRequest
{
    [Required, MaxLength(50)]
    [RegularExpression(@"^(\d{1,3}\.){3}\d{1,3}(\/\d{1,2})?$",
        ErrorMessage = "Must be a valid IPv4 address or CIDR range")]
    public string CidrRange { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Description { get; set; }
}

public class IpAllowlistResponse
{
    public Guid Id { get; set; }
    public string CidrRange { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DataRetentionPolicyRequest
{
    [Range(1, 25)]
    public int InvoiceRetentionYears { get; set; } = 7;

    [Range(1, 25)]
    public int AuditLogRetentionYears { get; set; } = 7;

    [Range(30, 3650)]
    public int LoginAttemptRetentionDays { get; set; } = 90;

    [Range(30, 3650)]
    public int NotificationRetentionDays { get; set; } = 90;

    public bool AutoDeleteEnabled { get; set; } = false;
}

public class GdprErasureRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class GdprErasureResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? AuditSummary { get; set; }
}

public class InvoiceLockResponse
{
    public bool IsLocked { get; set; }
    public string? LockedByName { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? Reason { get; set; }
}

public class UnlockInvoiceRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
