using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

// ── Feature 1: Refresh Tokens ─────────────────────────────────────────────────

public class RefreshRequest
{
    [Required]
    public string AccessToken { get; set; } = string.Empty;

    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid UserId { get; set; }
}

public class RevokeTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public class ActiveSessionResponse
{
    public Guid Id { get; set; }
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsCurrent { get; set; }
}

// ── Feature 2: Two-Factor Authentication ─────────────────────────────────────

// Step 1 login response when 2FA is required — no JWT yet
public class TwoFactorChallengeResponse
{
    public string TwoFactorSessionToken { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;       // "EmailOtp" | "Totp"
    public string? MaskedEmail { get; set; }                  // j***@acme.co.uk
    public DateTime ExpiresAt { get; set; }
    public bool RequiresTwoFactor { get; set; } = true;
}

// Step 2 — submit the code
public class VerifyTwoFactorRequest
{
    [Required]
    public string TwoFactorSessionToken { get; set; } = string.Empty;

    [Required, StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;
}

// Setup TOTP — returns QR data for the authenticator app
public class TotpSetupResponse
{
    public string SecretKey { get; set; } = string.Empty;    // base32 — shown once
    public string QrCodeUri { get; set; } = string.Empty;    // otpauth:// URI for QR code
    public string ManualEntryKey { get; set; } = string.Empty;
}

public class ConfirmTotpRequest
{
    [Required, StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;
}

public class TwoFactorStatusResponse
{
    public bool IsEnabled { get; set; }
    public string? Method { get; set; }
    public bool TotpConfirmed { get; set; }
}

public class ResendOtpRequest
{
    [Required]
    public string TwoFactorSessionToken { get; set; } = string.Empty;
}

// ── Feature 3: Password Reset ─────────────────────────────────────────────────

public class ForgotPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

// ── Feature 4: Login Throttling ───────────────────────────────────────────────

public class AccountStatusResponse
{
    public bool IsLocked { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public int FailedAttempts { get; set; }
    public int MaxAttempts { get; set; }
    public int RemainingAttempts { get; set; }
}

// ── Feature 5: API Keys ───────────────────────────────────────────────────────

public class CreateApiKeyRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = new();        // ["invoices:read", "invoices:write"]

    public DateTime? ExpiresAt { get; set; }                  // null = never expires
}

// Returned ONCE on creation — raw key never stored
public class CreateApiKeyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RawKey { get; set; } = string.Empty;       // e.g. ocri_xxxxxxxxxxxx — show once
    public string KeyPrefix { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Warning { get; set; } = "Store this key securely. It will not be shown again.";
}

public class ApiKeyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;    // only prefix shown in list
    public List<string> Scopes { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }
    public DateTime CreatedAt { get; set; }
}
