using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Services;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

/// <summary>
/// Daily 08:00 UTC: reminds uploaders about invoices approaching their due
/// date at the 7 / 3 / 1 day marks. DueReminderSent rows guarantee each
/// threshold fires at most once per invoice, and per-user notification
/// preferences are respected for both channels.
/// </summary>
[DisallowConcurrentExecution]
public class DueInvoiceReminderJob : IJob
{
    private static readonly int[] ReminderThresholds = [7, 3, 1];

    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<DueInvoiceReminderJob> _logger;

    private string AppUrl => _config["App:BaseUrl"] ?? "https://app.ocrinvoicesaas.com";

    public DueInvoiceReminderJob(
        ApplicationDbContext db,
        INotificationService notifications,
        IEmailService email,
        IConfiguration config,
        ILogger<DueInvoiceReminderJob> logger)
    {
        _db = db;
        _notifications = notifications;
        _email = email;
        _config = config;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("DueInvoiceReminderJob started at {Time}", DateTime.UtcNow);

        var today = DateTime.UtcNow.Date;
        var horizon = today.AddDays(ReminderThresholds.Max());

        var upcoming = await _db.Invoices
            .Include(i => i.UploadedByUser)
            .Include(i => i.Vendor)
            .Where(i =>
                i.DueDate != null &&
                i.DueDate >= today && i.DueDate <= horizon &&
                i.Status != InvoiceStatus.Approved && i.Status != InvoiceStatus.Failed)
            .ToListAsync();

        int sent = 0;

        foreach (var invoice in upcoming)
        {
            var daysUntilDue = (invoice.DueDate!.Value.Date - today).Days;
            var threshold = ReminderThresholds.FirstOrDefault(t => t == daysUntilDue);
            if (threshold == 0) continue;

            bool alreadySent = await _db.DueReminderSents.AnyAsync(r =>
                r.InvoiceId == invoice.Id && r.DaysBeforeDue == threshold);
            if (alreadySent) continue;

            var prefs = await _db.NotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == invoice.UploadedByUserId);

            var label = invoice.InvoiceNumber ?? invoice.FileName;
            var vendor = invoice.Vendor?.Name ?? invoice.ExtractedVendorName ?? "Unknown vendor";
            var amount = $"{invoice.Currency ?? "GBP"} {invoice.TotalAmount:N2}";

            if (prefs?.DueDateReminderInApp != false)
            {
                await _notifications.CreateAsync(
                    invoice.UploadedByUserId,
                    $"Invoice due in {threshold} day{(threshold == 1 ? "" : "s")}",
                    $"{label} ({vendor}, {amount}) is due on {invoice.DueDate:yyyy-MM-dd}.",
                    "Warning");
            }

            if (prefs?.DueDateReminderEmail != false)
            {
                try
                {
                    var body =
                        $"<h2>Invoice Due Soon</h2><p>Hi {invoice.UploadedByUser.FullName}, an invoice is due in {threshold} day{(threshold == 1 ? "" : "s")}.</p>" +
                        $"<div class='detail'><table><tr><td>Invoice #</td><td>{label}</td></tr>" +
                        $"<tr><td>Vendor</td><td>{vendor}</td></tr><tr><td>Amount</td><td>{amount}</td></tr>" +
                        $"<tr><td>Due Date</td><td>{invoice.DueDate:yyyy-MM-dd}</td></tr></table></div>" +
                        $"<a href='{AppUrl}/invoices/{invoice.Id}' class='btn'>View Invoice</a>";

                    await _email.SendAsync(
                        invoice.UploadedByUser.Email,
                        $"Invoice due in {threshold} day{(threshold == 1 ? "" : "s")}: {label}",
                        EmailTemplates.Render(body));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Due reminder email failed for invoice {Id}", invoice.Id);
                }
            }

            _db.DueReminderSents.Add(new DueReminderSent
            {
                InvoiceId = invoice.Id,
                DaysBeforeDue = threshold
            });
            await _db.SaveChangesAsync();
            sent++;
        }

        _logger.LogInformation("DueInvoiceReminderJob finished — {Count} reminders sent.", sent);
    }
}
