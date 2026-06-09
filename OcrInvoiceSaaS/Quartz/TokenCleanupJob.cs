using OcrInvoiceSaaS.Interfaces;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Services;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

/// <summary>
/// Runs nightly at 02:00 UTC to purge:
///   - Expired / used refresh tokens older than 7 days
///   - Used / expired password reset tokens older than 1 day
///   - Used / expired 2FA sessions older than 1 hour
///   - Login attempt records older than 30 days (keep for audit)
/// </summary>
[DisallowConcurrentExecution]
public class TokenCleanupJob : IJob
{
    private readonly ApplicationDbContext _db;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<TokenCleanupJob> _logger;

    public TokenCleanupJob(
        ApplicationDbContext db,
        IRefreshTokenService refreshTokenService,
        ILogger<TokenCleanupJob> logger)
    {
        _db = db;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("TokenCleanupJob started at {Time}", DateTime.UtcNow);

        // 1. Refresh tokens — purge inactive records older than 7 days
        await _refreshTokenService.PurgeExpiredAsync();

        // 2. Password reset tokens — purge used/expired older than 1 day
        var prtCutoff = DateTime.UtcNow.AddDays(-1);
        var deletedPrt = await _db.PasswordResetTokens
            .Where(t => (t.IsUsed || t.ExpiresAt < DateTime.UtcNow) && t.CreatedAt < prtCutoff)
            .ExecuteDeleteAsync();

        // 3. 2FA sessions — purge used/expired older than 1 hour
        var tfsCutoff = DateTime.UtcNow.AddHours(-1);
        var deletedTfs = await _db.TwoFactorSessions
            .Where(s => (s.IsUsed || s.ExpiresAt < DateTime.UtcNow) && s.CreatedAt < tfsCutoff)
            .ExecuteDeleteAsync();

        // 4. Login attempts — purge records older than 30 days
        var attemptCutoff = DateTime.UtcNow.AddDays(-30);
        var deletedAttempts = await _db.LoginAttempts
            .Where(a => a.AttemptedAt < attemptCutoff)
            .ExecuteDeleteAsync();

        _logger.LogInformation(
            "TokenCleanupJob finished. Deleted: {Prt} password tokens, {Tfs} 2FA sessions, {Attempts} login attempts.",
            deletedPrt, deletedTfs, deletedAttempts);
    }
}
