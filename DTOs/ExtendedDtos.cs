using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

public class PaginationQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? SortBy { get; set; } = "createdAt";
    public string? SortDir { get; set; } = "desc";
}

public class AddInvoiceItemRequest
{
    [Required, MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(0.0001, double.MaxValue)]
    public decimal Quantity { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    [Range(0, 100)]
    public decimal? TaxRate { get; set; }
}

public class UpdateInvoiceItemRequest
{
    [MaxLength(500)]
    public string? Description { get; set; }

    [Range(0.0001, double.MaxValue)]
    public decimal? Quantity { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? UnitPrice { get; set; }

    [Range(0, 100)]
    public decimal? TaxRate { get; set; }
}

public class SubscriptionPlanResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public int MaxInvoicesPerMonth { get; set; }
    public int MaxUsers { get; set; }
    public bool IncludesOcr { get; set; }
}

public class CreateCompanySubscriptionRequest
{
    [Required]
    public Guid SubscriptionPlanId { get; set; }

    [Required]
    public DateTime StartDate { get; set; }
}

public class CompanySubscriptionResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public SubscriptionPlanResponse Plan { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RecordPaymentRequest
{
    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [MaxLength(10)]
    public string Currency { get; set; } = "GBP";

    [MaxLength(100)]
    public string? PaymentReference { get; set; }

    [MaxLength(50)]
    public string? Provider { get; set; }

    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}

public class PaymentResponse
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
    public string? Provider { get; set; }
    public DateTime PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InviteMemberRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "Member";
}

public class CompanyMemberResponse
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}
