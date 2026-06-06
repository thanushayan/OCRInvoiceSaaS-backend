using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Services;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

[DisallowConcurrentExecution]
public class WeeklyDigestJob : IJob
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<WeeklyDigestJob> _logger;

    private string AppUrl => _config["App:BaseUrl"] ?? "https://app.ocrinvoicesaas.com";

    public WeeklyDigestJob(
        ApplicationDbContext db,
        IEmailService email,
        IConfiguration config,
        ILogger<WeeklyDigestJob> logger)
    {
        _db     = db;
        _email  = email;
        _config = config;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("WeeklyDigestJob started at {Time}", DateTime.UtcNow);

        var weekStart = DateTime.UtcNow.Date.AddDays(-7);
        var weekEnd   = DateTime.UtcNow.Date.AddDays(-1);

        var optedIn = await _db.NotificationPreferences
            .Include(p => p.User)
            .Where(p => p.WeeklyDigestEmail && p.User.IsActive)
            .ToListAsync();

        int sent = 0;

        foreach (var pref in optedIn)
        {
            try
            {
                var companyIds = await _db.CompanyUsers
                    .Where(cu => cu.UserId == pref.UserId)
                    .Select(cu => cu.CompanyId)
                    .ToListAsync();

                if (companyIds.Count == 0) continue;

                var invoices = await _db.Invoices
                    .Where(i =>
                        companyIds.Contains(i.CompanyId) &&
                        i.CreatedAt >= weekStart &&
                        i.CreatedAt <= weekEnd)
                    .ToListAsync();

                if (invoices.Count == 0) continue;

                var totalSpend   = invoices.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0);
                var approved     = invoices.Count(i => i.Status == InvoiceStatus.Approved);
                var pending      = invoices.Count(i => i.Status == InvoiceStatus.Uploaded || i.Status == InvoiceStatus.Processing);
                var duplicates   = invoices.Count(i => i.IsDuplicateFlagged);

                var html = BuildDigestEmail(
                    pref.User.FullName,
                    weekStart, weekEnd,
                    invoices.Count, approved, pending, duplicates,
                    totalSpend, AppUrl);

                await _email.SendAsync(
                    pref.User.Email,
                    $"Your weekly invoice digest — {weekStart:dd MMM} to {weekEnd:dd MMM}",
                    html);

                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed weekly digest for user {UserId}", pref.UserId);
            }
        }

        _logger.LogInformation("WeeklyDigestJob sent {Count} digest emails.", sent);
    }

    private static string BuildDigestEmail(
        string name, DateTime from, DateTime to,
        int total, int approved, int pending, int duplicates,
        decimal spend, string appUrl)
    {
        var duplicateWarning = duplicates > 0
            ? $"<div class='warning'>⚠ {duplicates} invoice{(duplicates == 1 ? "" : "s")} flagged as potential duplicates.</div>"
            : "";

        var body = $@"
<h2>Weekly Invoice Digest</h2>
<p>Hi {name}, here's your summary for <strong>{from:dd MMM} – {to:dd MMM yyyy}</strong>.</p>
<div class='detail'>
  <table>
    <tr><td>Total Invoices</td><td>{total}</td></tr>
    <tr><td>Approved</td><td style='color:#16a34a;'>{approved}</td></tr>
    <tr><td>Pending Review</td><td style='color:#d97706;'>{pending}</td></tr>
    <tr><td>Total Spend</td><td style='font-size:16px;'>GBP {spend:N2}</td></tr>
  </table>
</div>
{duplicateWarning}
<a href='{appUrl}/dashboard' class='btn'>View Dashboard</a>
<p style='font-size:12px;color:#6b7280;margin-top:20px;'>
  To stop receiving weekly digests, update your notification preferences in your account settings.
</p>";

        return EmailTemplates.Render(body);
    }
}
