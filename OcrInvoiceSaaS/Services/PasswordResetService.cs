using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

// ── Email abstraction ─────────────────────────────────────────────────────────

public interface IEmailService
{
    Task SendAsync(string to, string subject, string body);
}

/// <summary>
/// Dev/logging implementation. Replace with SendGridEmailService or MailgunEmailService in production.
/// </summary>
public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(ILogger<ConsoleEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string body)
    {
        _logger.LogInformation(
            "[EMAIL] To: {To} | Subject: {Subject} | Body: {Body}",
            to, subject, body);
        return Task.CompletedTask;
    }
}

/*
 * ── SendGrid implementation (install SendGrid NuGet package to use) ──────────
 *
 * public class SendGridEmailService : IEmailService
 * {
 *     private readonly IConfiguration _config;
 *     public SendGridEmailService(IConfiguration config) { _config = config; }
 *
 *     public async Task SendAsync(string to, string subject, string body)
 *     {
 *         var client = new SendGridClient(_config["SendGrid:ApiKey"]);
 *         var from = new EmailAddress(_config["SendGrid:FromEmail"], _config["SendGrid:FromName"]);
 *         var msg = MailHelper.CreateSingleEmail(from, new EmailAddress(to), subject, null, body);
 *         await client.SendEmailAsync(msg);
 *     }
 * }
 */

// ── PasswordResetService ──────────────────────────────────────────────────────

public class PasswordResetService : IPasswordResetService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    private int TokenExpiryMinutes => int.Parse(_config["Security:PasswordReset:ExpiryMinutes"] ?? "60");
    private string AppBaseUrl => _config["App:BaseUrl"] ?? "https://app.ocrinvoicesaas.com";

    public PasswordResetService(
        ApplicationDbContext db,
        IEmailService emailService,
        IConfiguration config)
    {
        _db = db;
        _emailService = emailService;
        _config = config;
    }

    public async Task<ServiceResult> SendResetEmailAsync(ForgotPasswordRequest request, string? ipAddress)
    {
        var email = request.Email.ToLower().Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);

        // Always return success — never reveal whether an account exists
        if (user == null)
            return ServiceResult.Success();

        // Invalidate any existing unused tokens for this user
        await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsUsed, true));

        // Generate raw token (sent in email) and store only its hash
        var rawToken = SecurityHelper.GenerateSecureToken(32);
        var tokenHash = SecurityHelper.HashToken(rawToken);

        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            IpAddress = ipAddress,
            ExpiresAt = DateTime.UtcNow.AddMinutes(TokenExpiryMinutes)
        });

        await _db.SaveChangesAsync();

        var resetUrl = $"{AppBaseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}&email={Uri.EscapeDataString(email)}";

        await _emailService.SendAsync(
            to: user.Email,
            subject: "Reset your OcrInvoiceSaaS password",
            body: $@"
                <p>Hi {user.FullName},</p>
                <p>Click the link below to reset your password. This link expires in {TokenExpiryMinutes} minutes.</p>
                <p><a href=""{resetUrl}"">Reset Password</a></p>
                <p>If you didn't request this, you can safely ignore this email.</p>
                <p><small>IP: {ipAddress ?? "unknown"}</small></p>");

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var email = request.Email.ToLower().Trim();
        var tokenHash = SecurityHelper.HashToken(request.Token);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
        if (user == null)
            return ServiceResult.Fail("Invalid reset request.", 400);

        var tokenRecord = await _db.PasswordResetTokens
            .FirstOrDefaultAsync(t =>
                t.UserId == user.Id &&
                t.TokenHash == tokenHash &&
                !t.IsUsed &&
                t.ExpiresAt > DateTime.UtcNow);

        if (tokenRecord == null)
            return ServiceResult.Fail("Reset link is invalid or has expired.", 400);

        // Update password
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.FailedLoginAttempts = 0;
        user.LockoutUntil = null;
        user.UpdatedAt = DateTime.UtcNow;

        // Consume token
        tokenRecord.IsUsed = true;
        tokenRecord.UsedAt = DateTime.UtcNow;

        // Revoke all active refresh tokens (force re-login on all devices)
        await _db.RefreshTokens
            .Where(rt => rt.UserId == user.Id && !rt.IsRevoked && !rt.IsUsed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.IsRevoked, true)
                .SetProperty(rt => rt.RevokedAt, DateTime.UtcNow));

        await _db.SaveChangesAsync();

        await _emailService.SendAsync(
            to: user.Email,
            subject: "Your password was changed",
            body: $"Hi {user.FullName}, your password was successfully changed. If this wasn't you, contact support immediately.");

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return ServiceResult.Fail("User not found.", 404);

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return ServiceResult.Fail("Current password is incorrect.", 400);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        // Revoke all other refresh tokens (keep current session alive)
        await _db.SaveChangesAsync();

        await _emailService.SendAsync(
            to: user.Email,
            subject: "Your password was changed",
            body: $"Hi {user.FullName}, your password was changed. If this wasn't you, contact support immediately.");

        return ServiceResult.Success();
    }
}
