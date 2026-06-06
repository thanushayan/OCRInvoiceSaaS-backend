using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

public class CreateCompanyRequest
{
    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? RegistrationNumber { get; set; }

    [MaxLength(50)]
    public string? VatNumber { get; set; }

    public string? Address { get; set; }

    [Phone]
    public string? Phone { get; set; }

    [EmailAddress]
    public string? Email { get; set; }
}

public class CompanyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public string? VatNumber { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string UserRole { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateVendorRequest
{
    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [EmailAddress]
    public string? ContactEmail { get; set; }

    public string? Phone { get; set; }
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? VatNumber { get; set; }
}

public class VendorResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? VatNumber { get; set; }
    public DateTime CreatedAt { get; set; }
}
