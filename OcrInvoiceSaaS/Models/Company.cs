namespace OcrInvoiceSaaS.Models;

public class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public string? VatNumber { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CompanyUser> CompanyUsers { get; set; } = new List<CompanyUser>();
    public ICollection<Vendor> Vendors { get; set; } = new List<Vendor>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<ExpenseCategory> ExpenseCategories { get; set; } = new List<ExpenseCategory>();
    public ICollection<CompanySubscription> CompanySubscriptions { get; set; } = new List<CompanySubscription>();
}
