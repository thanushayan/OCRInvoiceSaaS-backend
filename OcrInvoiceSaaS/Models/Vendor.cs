namespace OcrInvoiceSaaS.Models;

public class Vendor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? VatNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
