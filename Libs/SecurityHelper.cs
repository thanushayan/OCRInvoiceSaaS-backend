using System.Security.Cryptography;
using System.Text;
using OtpNet;

namespace OcrInvoiceSaaS.Libs;

public static class SecurityHelper
{
    public static string GenerateSecureToken(int byteLength = 64)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string GenerateNumericOtp()
        => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

    public static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string GenerateTotpSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

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

    public static string BuildTotpUri(string base32Secret, string email, string issuer = "OcrInvoiceSaaS")
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedEmail = Uri.EscapeDataString(email);
        return $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={base32Secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    public static (string rawKey, string prefix, string hash) GenerateApiKey()
    {
        var random = GenerateSecureToken(32);
        var raw = $"ocri_{random}";
        var prefix = raw[..12];
        var hash = HashToken(raw);
        return (raw, prefix, hash);
    }

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
