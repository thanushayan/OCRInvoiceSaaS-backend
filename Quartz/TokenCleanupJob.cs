using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Services;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

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

        await _refreshTokenService.PurgeExpiredAsync();

        var prtCutoff = DateTime.UtcNow.AddDays(-1);
        var deletedPrt = await _db.PasswordResetTokens
            .Where(t => (t.IsUsed || t.ExpiresAt < DateTime.UtcNow) && t.CreatedAt < prtCutoff)
            .ExecuteDeleteAsync();

        var tfsCutoff = DateTime.UtcNow.AddHours(-1);
        var deletedTfs = await _db.TwoFactorSessions
            .Where(s => (s.IsUsed || s.ExpiresAt < DateTime.UtcNow) && s.CreatedAt < tfsCutoff)
            .ExecuteDeleteAsync();

        var attemptCutoff = DateTime.UtcNow.AddDays(-30);
        var deletedAttempts = await _db.LoginAttempts
            .Where(a => a.AttemptedAt < attemptCutoff)
            .ExecuteDeleteAsync();

        _logger.LogInformation(
            "TokenCleanupJob finished. Deleted: {Prt} password tokens, {Tfs} 2FA sessions, {Attempts} login attempts.",
            deletedPrt, deletedTfs, deletedAttempts);
    }
}
