using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class LoginThrottleService : ILoginThrottleService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    private int MaxFailedAttempts => int.Parse(_config["Security:Lockout:MaxFailedAttempts"] ?? "5");
    private int LockoutMinutes => int.Parse(_config["Security:Lockout:LockoutMinutes"] ?? "15");
    private int WindowMinutes => int.Parse(_config["Security:Lockout:WindowMinutes"] ?? "10");

    public LoginThrottleService(ApplicationDbContext db, IConfiguration config) { _db = db; _config = config; }

    public async Task<ServiceResult<AccountStatusResponse>> CheckAsync(string email)
    {
        var normalised = email.ToLower().Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalised);

        if (user != null && user.IsLockedOut)
            return ServiceResult<AccountStatusResponse>.Fail($"Account is locked. Try again after {user.LockoutUntil:HH:mm UTC}.", 429);

        var since = DateTime.UtcNow.AddMinutes(-WindowMinutes);
        var recentFailures = await _db.LoginAttempts
            .CountAsync(a => a.Email == normalised && !a.Succeeded && a.AttemptedAt >= since);

        if (recentFailures >= MaxFailedAttempts)
        {
            if (user != null) { user.LockoutUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes); user.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
            return ServiceResult<AccountStatusResponse>.Fail($"Too many failed attempts. Account locked for {LockoutMinutes} minutes.", 429);
        }

        return ServiceResult<AccountStatusResponse>.Success(new AccountStatusResponse
        { IsLocked = false, FailedAttempts = recentFailures, MaxAttempts = MaxFailedAttempts, RemainingAttempts = Math.Max(0, MaxFailedAttempts - recentFailures) });
    }

    public async Task RecordSuccessAsync(string email, string? ipAddress)
    {
        var normalised = email.ToLower().Trim();
        _db.LoginAttempts.Add(new LoginAttempt { Email = normalised, IpAddress = ipAddress, Succeeded = true });
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalised);
        if (user != null) { user.FailedLoginAttempts = 0; user.LockoutUntil = null; user.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync();
    }

    public async Task RecordFailureAsync(string email, string? ipAddress, string reason)
    {
        var normalised = email.ToLower().Trim();
        _db.LoginAttempts.Add(new LoginAttempt { Email = normalised, IpAddress = ipAddress, Succeeded = false, FailureReason = reason });
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalised);
        if (user != null)
        {
            user.FailedLoginAttempts++;
            user.UpdatedAt = DateTime.UtcNow;
            if (user.FailedLoginAttempts >= MaxFailedAttempts) user.LockoutUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
        }
        await _db.SaveChangesAsync();
    }

    public async Task UnlockAccountAsync(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return;
        user.FailedLoginAttempts = 0; user.LockoutUntil = null; user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
