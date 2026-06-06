namespace OcrInvoiceSaaS.Models;

// ── Feature 1: Refresh Tokens ─────────────────────────────────────────────────

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public bool IsRevoked { get; set; } = false;
    public bool IsUsed { get; set; } = false;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenId { get; set; }

    public User User { get; set; } = null!;

    public bool IsActive => !IsRevoked && !IsUsed && DateTime.UtcNow < ExpiresAt;
}

// ── Feature 2: Two-Factor Authentication ─────────────────────────────────────

public enum TwoFactorMethod { EmailOtp, Totp }

public class UserTwoFactor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public bool IsEnabled { get; set; } = false;
    public TwoFactorMethod Method { get; set; } = TwoFactorMethod.EmailOtp;
    public string? TotpSecretKey { get; set; }
    public bool TotpConfirmed { get; set; } = false;
    public string? EmailOtpCodeHash { get; set; }
    public DateTime? EmailOtpExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

public class TwoFactorSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public bool IsUsed { get; set; } = false;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

// ── Feature 3: Password Reset ─────────────────────────────────────────────────

public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public bool IsUsed { get; set; } = false;
    public string? IpAddress { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }

    public User User { get; set; } = null!;

    public bool IsValid => !IsUsed && DateTime.UtcNow < ExpiresAt;
}

// ── Feature 4: Login Throttling / Account Lockout ─────────────────────────────

public class LoginAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}

// ── Feature 5: API Keys ───────────────────────────────────────────────────────

public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    public User User { get; set; } = null!;
    public Company Company { get; set; } = null!;

    public bool IsValid => IsActive && (ExpiresAt == null || DateTime.UtcNow < ExpiresAt);
}
