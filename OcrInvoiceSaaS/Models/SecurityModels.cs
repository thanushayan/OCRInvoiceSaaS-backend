namespace OcrInvoiceSaaS.Models;

// ── Feature 1: Refresh Tokens ─────────────────────────────────────────────────

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;       // cryptographically random, stored hashed
    public string TokenHash { get; set; } = string.Empty;   // SHA-256 of Token
    public string? DeviceInfo { get; set; }                  // User-Agent snapshot
    public string? IpAddress { get; set; }
    public bool IsRevoked { get; set; } = false;
    public bool IsUsed { get; set; } = false;                // one-time use — rotation on each refresh
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenId { get; set; }           // chain tracking

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

    // TOTP fields (Google Authenticator style)
    public string? TotpSecretKey { get; set; }              // base32 secret, stored encrypted
    public bool TotpConfirmed { get; set; } = false;        // true after user verifies first TOTP code

    // Email OTP fields
    public string? EmailOtpCodeHash { get; set; }           // bcrypt hash of 6-digit code
    public DateTime? EmailOtpExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

// Pending 2FA session — issued after password check, before 2FA verification
public class TwoFactorSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string SessionToken { get; set; } = string.Empty; // short-lived opaque token
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
    public string TokenHash { get; set; } = string.Empty;    // SHA-256 of the raw token sent by email
    public bool IsUsed { get; set; } = false;
    public string? IpAddress { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }

    public User User { get; set; } = null!;

    public bool IsValid => !IsUsed && DateTime.UtcNow < ExpiresAt;
}

// ── Feature 4: Login Throttling / Account Lockout ────────────────────────────

public class LoginAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;        // normalised lowercase
    public string? IpAddress { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}

// Lockout state tracked on the User — added via migration extension
// (fields added to User model via partial — see UserExtensions.cs)

// ── Feature 5: API Keys ───────────────────────────────────────────────────────

public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;          // e.g. "Xero Integration"
    public string KeyPrefix { get; set; } = string.Empty;     // first 8 chars shown in UI: ocri_xxxx
    public string KeyHash { get; set; } = string.Empty;       // SHA-256 of full key; never stored plain
    public string[] Scopes { get; set; } = [];                 // e.g. ["invoices:read", "invoices:write"]
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }                   // null = never
    public DateTime? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    public User User { get; set; } = null!;
    public Company Company { get; set; } = null!;

    public bool IsValid => IsActive && (ExpiresAt == null || DateTime.UtcNow < ExpiresAt);
}
