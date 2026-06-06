using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string body);
}

public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;
    public ConsoleEmailService(ILogger<ConsoleEmailService> logger) { _logger = logger; }

    public Task SendAsync(string to, string subject, string body)
    {
        _logger.LogInformation("[EMAIL] To: {To} | Subject: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}

public class PasswordResetService : IPasswordResetService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    private int TokenExpiryMinutes => int.Parse(_config["Security:PasswordReset:ExpiryMinutes"] ?? "60");
    private string AppBaseUrl => _config["App:BaseUrl"] ?? "https://app.ocrinvoicesaas.com";

    public PasswordResetService(ApplicationDbContext db, IEmailService emailService, IConfiguration config)
    {
        _db = db; _emailService = emailService; _config = config;
    }

    public async Task<ServiceResult> SendResetEmailAsync(ForgotPasswordRequest request, string? ipAddress)
    {
        var email = request.Email.ToLower().Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
        if (user == null) return ServiceResult.Success();

        await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsUsed, true));

        var rawToken = SecurityHelper.GenerateSecureToken(32);
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id, TokenHash = SecurityHelper.HashToken(rawToken),
            IpAddress = ipAddress, ExpiresAt = DateTime.UtcNow.AddMinutes(TokenExpiryMinutes)
        });

        await _db.SaveChangesAsync();
        var resetUrl = $"{AppBaseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}&email={Uri.EscapeDataString(email)}";
        await _emailService.SendAsync(user.Email, "Reset your password", $"<p>Click <a href='{resetUrl}'>here</a> to reset your password. Expires in {TokenExpiryMinutes} minutes.</p>");
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var email = request.Email.ToLower().Trim();
        var tokenHash = SecurityHelper.HashToken(request.Token);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
        if (user == null) return ServiceResult.Fail("Invalid reset request.", 400);

        var tokenRecord = await _db.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.UserId == user.Id && t.TokenHash == tokenHash && !t.IsUsed && t.ExpiresAt > DateTime.UtcNow);
        if (tokenRecord == null) return ServiceResult.Fail("Reset link is invalid or has expired.", 400);

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.FailedLoginAttempts = 0; user.LockoutUntil = null; user.UpdatedAt = DateTime.UtcNow;
        tokenRecord.IsUsed = true; tokenRecord.UsedAt = DateTime.UtcNow;

        await _db.RefreshTokens
            .Where(rt => rt.UserId == user.Id && !rt.IsRevoked && !rt.IsUsed)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.IsRevoked, true).SetProperty(rt => rt.RevokedAt, DateTime.UtcNow));

        await _db.SaveChangesAsync();
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
        await _db.SaveChangesAsync();
        return ServiceResult.Success();
    }
}
