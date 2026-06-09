using System.Security.Cryptography;
using System.Text;
using OtpNet;

namespace OcrInvoiceSaaS.Libs;

public static class SecurityHelper
{
    // ── Cryptographically random tokens ──────────────────────────────────────

    /// <summary>Generates a URL-safe random token of the specified byte length.</summary>
    public static string GenerateSecureToken(int byteLength = 64)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Generates a 6-digit numeric OTP.</summary>
    public static string GenerateNumericOtp()
        => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    // ── Hashing ───────────────────────────────────────────────────────────────

    /// <summary>SHA-256 hash as hex string. Used to store token hashes in the DB.</summary>
    public static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── TOTP (RFC 6238 — Google Authenticator compatible) ────────────────────

    /// <summary>Generates a new base32 TOTP secret key.</summary>
    public static string GenerateTotpSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20); // 160 bits
        return Base32Encoding.ToString(key);
    }

    /// <summary>Validates a 6-digit TOTP code against the secret. Accepts ±1 time step.</summary>
    public static bool ValidateTotpCode(string base32Secret, string code)
    {
        try
        {
            var keyBytes = Base32Encoding.ToBytes(base32Secret);
            var totp = new Totp(keyBytes);
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds an otpauth:// URI for QR code generation.
    /// Pass this URI to a QR code library (e.g. QRCoder) or return it to the frontend.
    /// </summary>
    public static string BuildTotpUri(string base32Secret, string email, string issuer = "OcrInvoiceSaaS")
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedEmail = Uri.EscapeDataString(email);
        return $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={base32Secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    // ── API Key formatting ────────────────────────────────────────────────────

    /// <summary>
    /// Generates a structured API key: ocri_{random}
    /// Returns (rawKey, prefix, hash).
    /// </summary>
    public static (string rawKey, string prefix, string hash) GenerateApiKey()
    {
        var random = GenerateSecureToken(32);
        var raw = $"ocri_{random}";
        var prefix = raw[..12]; // "ocri_xxxxxxx" shown in UI
        var hash = HashToken(raw);
        return (raw, prefix, hash);
    }

    // ── Email masking ─────────────────────────────────────────────────────────

    /// <summary>Returns j***@acme.co.uk format for displaying in 2FA prompts.</summary>
    public static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2) return "***";
        var local = parts[0];
        var masked = local.Length <= 2
            ? new string('*', local.Length)
            : local[0] + new string('*', local.Length - 2) + local[^1];
        return $"{masked}@{parts[1]}";
    }
}
