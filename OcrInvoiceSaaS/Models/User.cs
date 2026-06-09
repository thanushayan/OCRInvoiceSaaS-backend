namespace OcrInvoiceSaaS.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Feature 4: Lockout
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockoutUntil { get; set; }
    public bool IsLockedOut => LockoutUntil.HasValue && DateTime.UtcNow < LockoutUntil.Value;

    // Feature 2: 2FA flag
    public bool TwoFactorEnabled { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CompanyUser> CompanyUsers { get; set; } = new List<CompanyUser>();
    public ICollection<Invoice> UploadedInvoices { get; set; } = new List<Invoice>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
    public UserTwoFactor? TwoFactor { get; set; }
}
