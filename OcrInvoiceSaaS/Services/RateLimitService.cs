using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class RateLimitService : IRateLimitService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    // Defaults when no subscription plan applies
    private const int DefaultHourlyLimit = 1000;

    public RateLimitService(ApplicationDbContext db, IConfiguration config)
    {
        _db     = db;
        _config = config;
    }

    public async Task<(bool allowed, RateLimitStatusResponse status)> CheckAndIncrementAsync(
        Guid companyId, string resource = "api")
    {
        var limit = await GetLimitForCompanyAsync(companyId);

        // Window = current UTC hour
        var windowStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month,
            DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0, DateTimeKind.Utc);
        var windowEnd    = windowStart.AddHours(1);
        var windowKey    = $"{windowStart:yyyyMMddHH}:{resource}";

        var record = await _db.ApiUsageRecords
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.WindowKey == windowKey);

        if (record == null)
        {
            record = new ApiUsageRecord
            {
                CompanyId    = companyId,
                WindowKey    = windowKey,
                RequestCount = 0,
                WindowStart  = windowStart,
                WindowEnd    = windowEnd
            };
            _db.ApiUsageRecords.Add(record);
        }

        bool allowed = record.RequestCount < limit;

        if (allowed)
        {
            record.RequestCount++;
            record.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var statusResponse = new RateLimitStatusResponse
        {
            RequestsUsed      = record.RequestCount,
            RequestsLimit     = limit,
            RequestsRemaining = Math.Max(0, limit - record.RequestCount),
            PercentUsed       = Math.Round((double)record.RequestCount / limit * 100, 1),
            WindowResetAt     = windowEnd,
            IsThrottled       = !allowed
        };

        return (allowed, statusResponse);
    }

    public async Task<ServiceResult<RateLimitStatusResponse>> GetStatusAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<RateLimitStatusResponse>.Fail("Access denied.", 403);

        var (_, status) = await CheckAndIncrementAsync(companyId);
        return ServiceResult<RateLimitStatusResponse>.Success(status);
    }

    private async Task<int> GetLimitForCompanyAsync(Guid companyId)
    {
        var activeSub = await _db.CompanySubscriptions
            .Include(s => s.SubscriptionPlan)
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.IsActive);

        if (activeSub?.SubscriptionPlan == null) return DefaultHourlyLimit;

        // Map plan to hourly rate limits
        return activeSub.SubscriptionPlan.Name.ToLower() switch
        {
            "starter"    => int.Parse(_config["RateLimit:Starter"]    ?? "500"),
            "growth"     => int.Parse(_config["RateLimit:Growth"]     ?? "2000"),
            "enterprise" => int.Parse(_config["RateLimit:Enterprise"] ?? "10000"),
            _            => DefaultHourlyLimit
        };
    }
}
