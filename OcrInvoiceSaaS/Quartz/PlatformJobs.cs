using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Services;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

/// <summary>
/// Runs every 5 minutes to retry pending/failed webhook deliveries.
/// </summary>
[DisallowConcurrentExecution]
public class WebhookRetryJob : IJob
{
    private readonly IWebhookService _webhookService;
    private readonly ILogger<WebhookRetryJob> _logger;

    public WebhookRetryJob(IWebhookService webhookService, ILogger<WebhookRetryJob> logger)
    {
        _webhookService = webhookService;
        _logger         = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogDebug("WebhookRetryJob triggered at {Time}", DateTime.UtcNow);
        await _webhookService.ProcessPendingDeliveriesAsync();
    }
}

/// <summary>
/// Runs daily at 08:00 UTC — sends reminder emails for invoices with
/// due dates in 7 days, 3 days, and 1 day. Prevents duplicate sends
/// via DueReminderSent table.
/// </summary>
[DisallowConcurrentExecution]
public class DueInvoiceReminderJob : IJob
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notifications;
    private readonly ILogger<DueInvoiceReminderJob> _logger;

    private static readonly int[] ReminderDays = [7, 3, 1];

    public DueInvoiceReminderJob(
        ApplicationDbContext db,
        IEmailService emailService,
        INotificationService notifications,
        ILogger<DueInvoiceReminderJob> logger)
    {
        _db            = db;
        _emailService  = emailService;
        _notifications = notifications;
        _logger        = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("DueInvoiceReminderJob started at {Time}", DateTime.UtcNow);

        int sent = 0;

        foreach (var daysBeforeDue in ReminderDays)
        {
            var targetDate = DateTime.UtcNow.Date.AddDays(daysBeforeDue);

            // Find all invoices due on targetDate that haven't been reminded yet
            var invoices = await _db.Invoices
                .Include(i => i.UploadedByUser)
                .Include(i => i.Vendor)
                .Include(i => i.Company)
                .Where(i =>
                    i.DueDate.HasValue &&
                    i.DueDate.Value.Date == targetDate &&
                    i.Status != InvoiceStatus.Approved &&
                    i.Status != InvoiceStatus.Failed)
                .ToListAsync();

            foreach (var invoice in invoices)
            {
                // Check if reminder was already sent for this invoice + threshold
                bool alreadySent = await _db.DueRemindersSent.AnyAsync(r =>
                    r.InvoiceId == invoice.Id && r.DaysBeforeDue == daysBeforeDue);

                if (alreadySent) continue;

                var vendorName = invoice.Vendor?.Name ?? invoice.ExtractedVendorName ?? "Unknown Vendor";
                var amount     = invoice.TotalAmount?.ToString("F2") ?? "—";
                var currency   = invoice.Currency ?? "GBP";

                // Send email to the uploader
                await _emailService.SendAsync(
                    to: invoice.UploadedByUser.Email,
                    subject: $"Invoice due in {daysBeforeDue} day{(daysBeforeDue == 1 ? "" : "s")}: {invoice.InvoiceNumber ?? invoice.FileName}",
                    body: $@"
                        <p>Hi {invoice.UploadedByUser.FullName},</p>
                        <p>This is a reminder that the following invoice is due in <strong>{daysBeforeDue} day{(daysBeforeDue == 1 ? "" : "s")}</strong>:</p>
                        <table style=""border-collapse:collapse"">
                          <tr><td style=""padding:4px 12px 4px 0""><strong>Invoice:</strong></td><td>{invoice.InvoiceNumber ?? invoice.FileName}</td></tr>
                          <tr><td style=""padding:4px 12px 4px 0""><strong>Vendor:</strong></td><td>{vendorName}</td></tr>
                          <tr><td style=""padding:4px 12px 4px 0""><strong>Amount:</strong></td><td>{currency} {amount}</td></tr>
                          <tr><td style=""padding:4px 12px 4px 0""><strong>Due Date:</strong></td><td>{invoice.DueDate:dd MMM yyyy}</td></tr>
                          <tr><td style=""padding:4px 12px 4px 0""><strong>Status:</strong></td><td>{invoice.Status}</td></tr>
                        </table>
                        <p><a href=""https://app.ocrinvoicesaas.com/invoices/{invoice.Id}"">View Invoice</a></p>");

                // In-app notification
                await _notifications.CreateAsync(
                    invoice.UploadedByUserId,
                    $"Invoice due in {daysBeforeDue} day{(daysBeforeDue == 1 ? "" : "s")}",
                    $"{invoice.InvoiceNumber ?? invoice.FileName} from {vendorName} — {currency} {amount}",
                    daysBeforeDue == 1 ? "Warning" : "Info");

                // Mark as sent
                _db.DueRemindersSent.Add(new DueReminderSent
                {
                    InvoiceId    = invoice.Id,
                    DaysBeforeDue = daysBeforeDue
                });

                sent++;
            }
        }

        if (sent > 0) await _db.SaveChangesAsync();
        _logger.LogInformation("DueInvoiceReminderJob sent {Count} reminders.", sent);
    }
}
