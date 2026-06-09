namespace OcrInvoiceSaaS.Models;

public class CompanyUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Member"; // Owner, Admin, Member
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public User User { get; set; } = null!;
}
