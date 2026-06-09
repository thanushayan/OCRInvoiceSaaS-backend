using OcrInvoiceSaaS.Interfaces;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OcrInvoiceSaaS.Services;

// ══════════════════════════════════════════════════════════════════════════════
// Interface (already defined in PasswordResetService.cs — re-declared here
// for clarity. In a real project, move IEmailService to Interfaces/)
// ══════════════════════════════════════════════════════════════════════════════

// ── HTML Email Template Engine ────────────────────────────────────────────────

public static class EmailTemplates
{
    private const string BaseLayout = @"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width,initial-scale=1'>
  <style>
    body {{ margin:0; padding:0; background:#f4f6f9; font-family:Helvetica,Arial,sans-serif; }}
    .wrap {{ max-width:600px; margin:32px auto; background:#fff; border-radius:8px;
             border:1px solid #e5e7eb; overflow:hidden; }}
    .header {{ background:#1a56db; padding:24px 32px; }}
    .header h1 {{ margin:0; color:#fff; font-size:20px; font-weight:600; }}
    .header p {{ margin:4px 0 0; color:#bfdbfe; font-size:13px; }}
    .body {{ padding:32px; color:#1f2937; font-size:14px; line-height:1.6; }}
    .body h2 {{ font-size:17px; margin:0 0 12px; color:#111827; }}
    .detail {{ background:#f9fafb; border:1px solid #e5e7eb; border-radius:6px;
               padding:16px; margin:16px 0; }}
    .detail table {{ width:100%; border-collapse:collapse; font-size:13px; }}
    .detail td {{ padding:5px 0; }}
    .detail td:first-child {{ color:#6b7280; width:140px; }}
    .detail td:last-child {{ color:#111827; font-weight:500; }}
    .btn {{ display:inline-block; background:#1a56db; color:#fff; text-decoration:none;
            padding:10px 22px; border-radius:6px; font-size:14px; font-weight:500;
            margin:16px 0; }}
    .btn-danger {{ background:#dc2626; }}
    .btn-success {{ background:#16a34a; }}
    .warning {{ background:#fef9c3; border:1px solid #fde047; border-radius:6px;
                padding:12px 16px; font-size:13px; color:#713f12; margin:16px 0; }}
    .footer {{ background:#f9fafb; padding:16px 32px; border-top:1px solid #e5e7eb;
               font-size:12px; color:#9ca3af; text-align:center; }}
  </style>
</head>
<body>
  <div class='wrap'>
    <div class='header'>
      <h1>OCR Invoice SaaS</h1>
      <p>{companyName}</p>
    </div>
    <div class='body'>
      {body}
    </div>
    <div class='footer'>
      {footer}
    </div>
  </div>
</body>
</html>";

    public static string Render(string body, string companyName = "OCR Invoice SaaS",
        string footer = "You're receiving this because you're a member of this workspace.<br>© OCR Invoice SaaS Ltd")
        => BaseLayout
            .Replace("{companyName}", companyName)
            .Replace("{body}", body)
            .Replace("{footer}", footer);

    // ── Pre-built templates ────────────────────────────────────────────────────

    public static (string subject, string html) OcrCompleted(
        string recipientName, string fileName, string invoiceNumber, string status, string appUrl, Guid invoiceId)
    {
        var statusColor = status == "Processed" ? "#16a34a" : "#dc2626";
        var body = $@"
<h2>OCR Processing Complete</h2>
<p>Hi {recipientName}, the OCR extraction for your invoice has finished.</p>
<div class='detail'>
  <table>
    <tr><td>File</td><td>{fileName}</td></tr>
    <tr><td>Invoice #</td><td>{invoiceNumber}</td></tr>
    <tr><td>Status</td><td style='color:{statusColor};'>{status}</td></tr>
    <tr><td>Processed</td><td>{DateTime.UtcNow:dd MMM yyyy HH:mm} UTC</td></tr>
  </table>
</div>
<a href='{appUrl}/invoices/{invoiceId}' class='btn'>View Invoice</a>";

        return ($"OCR complete: {fileName}", Render(body));
    }

    public static (string subject, string html) ApprovalRequired(
        string recipientName, string stepName, string invoiceNumber,
        string vendorName, string amount, Guid instanceId, string appUrl)
    {
        var body = $@"
<h2>Approval Required</h2>
<p>Hi {recipientName}, your action is needed on the following invoice.</p>
<div class='detail'>
  <table>
    <tr><td>Step</td><td>{stepName}</td></tr>
    <tr><td>Invoice #</td><td>{invoiceNumber}</td></tr>
    <tr><td>Vendor</td><td>{vendorName}</td></tr>
    <tr><td>Amount</td><td>{amount}</td></tr>
  </table>
</div>
<a href='{appUrl}/approvals/{instanceId}' class='btn'>Review &amp; Approve</a>
<p style='font-size:12px;color:#6b7280;margin-top:16px;'>
  This step will time out if not actioned within the configured window.
</p>";

        return ($"Approval required: {invoiceNumber} — {stepName}", Render(body));
    }

    public static (string subject, string html) InvoiceApproved(
        string recipientName, string invoiceNumber, string vendorName, string amount,
        Guid invoiceId, string appUrl)
    {
        var body = $@"
<h2>Invoice Approved ✓</h2>
<p>Hi {recipientName}, your invoice has been fully approved.</p>
<div class='detail'>
  <table>
    <tr><td>Invoice #</td><td>{invoiceNumber}</td></tr>
    <tr><td>Vendor</td><td>{vendorName}</td></tr>
    <tr><td>Amount</td><td>{amount}</td></tr>
    <tr><td>Approved</td><td>{DateTime.UtcNow:dd MMM yyyy}</td></tr>
  </table>
</div>
<a href='{appUrl}/invoices/{invoiceId}' class='btn btn-success'>View Invoice</a>";

        return ($"Invoice approved: {invoiceNumber}", Render(body));
    }

    public static (string subject, string html) InvoiceRejected(
        string recipientName, string invoiceNumber, string stepName,
        string reason, Guid invoiceId, string appUrl)
    {
        var body = $@"
<h2>Invoice Rejected</h2>
<p>Hi {recipientName}, an invoice has been rejected during the approval workflow.</p>
<div class='detail'>
  <table>
    <tr><td>Invoice #</td><td>{invoiceNumber}</td></tr>
    <tr><td>Rejected at</td><td>{stepName}</td></tr>
    <tr><td>Reason</td><td>{reason}</td></tr>
  </table>
</div>
<div class='warning'>The invoice has been returned to Processed status for review and correction.</div>
<a href='{appUrl}/invoices/{invoiceId}' class='btn btn-danger'>View Invoice</a>";

        return ($"Invoice rejected: {invoiceNumber}", Render(body));
    }

    public static (string subject, string html) DuplicateDetected(
        string recipientName, string invoiceNumber, string fileName,
        string matchReason, decimal matchScore, Guid invoiceId, string appUrl)
    {
        var body = $@"
<h2>Potential Duplicate Invoice Detected</h2>
<p>Hi {recipientName}, a newly uploaded invoice may be a duplicate of an existing record.</p>
<div class='detail'>
  <table>
    <tr><td>New Invoice</td><td>{fileName}</td></tr>
    <tr><td>Invoice #</td><td>{invoiceNumber}</td></tr>
    <tr><td>Match Reason</td><td>{matchReason}</td></tr>
    <tr><td>Confidence</td><td>{matchScore:0}%</td></tr>
  </table>
</div>
<div class='warning'>Please review and either confirm or dismiss this duplicate flag before approving.</div>
<a href='{appUrl}/invoices/{invoiceId}' class='btn'>Review Duplicate</a>";

        return ($"Duplicate detected: {invoiceNumber}", Render(body));
    }

    public static (string subject, string html) PaymentDue(
        string recipientName, string invoiceNumber, string vendorName,
        string amount, string dueDate, int daysUntilDue, Guid invoiceId, string appUrl)
    {
        var urgencyColor = daysUntilDue <= 1 ? "#dc2626" : daysUntilDue <= 3 ? "#d97706" : "#1a56db";
        var body = $@"
<h2 style='color:{urgencyColor};'>Invoice Due in {daysUntilDue} Day{(daysUntilDue == 1 ? "" : "s")}</h2>
<p>Hi {recipientName}, a payment is due soon.</p>
<div class='detail'>
  <table>
    <tr><td>Invoice #</td><td>{invoiceNumber}</td></tr>
    <tr><td>Vendor</td><td>{vendorName}</td></tr>
    <tr><td>Amount</td><td>{amount}</td></tr>
    <tr><td>Due Date</td><td style='color:{urgencyColor};font-weight:700;'>{dueDate}</td></tr>
  </table>
</div>
<a href='{appUrl}/invoices/{invoiceId}' class='btn'>View Invoice</a>";

        return ($"Payment due in {daysUntilDue} day{(daysUntilDue == 1 ? "" : "s")}: {invoiceNumber} — {amount}", Render(body));
    }

    public static (string subject, string html) BulkOcrComplete(
        string recipientName, int total, int processed, int failed, Guid jobId, string appUrl)
    {
        var statusColor = failed == 0 ? "#16a34a" : "#d97706";
        var body = $@"
<h2>Bulk OCR Job Complete</h2>
<p>Hi {recipientName}, your bulk OCR processing job has finished.</p>
<div class='detail'>
  <table>
    <tr><td>Total Invoices</td><td>{total}</td></tr>
    <tr><td>Processed</td><td style='color:#16a34a;'>{processed}</td></tr>
    <tr><td>Failed</td><td style='color:{(failed > 0 ? "#dc2626" : "#6b7280")};'>{failed}</td></tr>
    <tr><td>Success Rate</td><td style='color:{statusColor};'>{(total > 0 ? Math.Round((double)processed / total * 100, 0) : 0)}%</td></tr>
  </table>
</div>
<a href='{appUrl}/bulk-ocr/{jobId}' class='btn'>View Results</a>";

        return ($"Bulk OCR complete: {processed}/{total} invoices processed", Render(body));
    }

    public static (string subject, string html) MentionNotification(
        string recipientName, string mentionedByName, string invoiceNumber,
        string commentPreview, Guid invoiceId, string appUrl)
    {
        var body = $@"
<h2>You were mentioned</h2>
<p>Hi {recipientName}, <strong>{mentionedByName}</strong> mentioned you in a comment on invoice {invoiceNumber}.</p>
<div class='detail' style='border-left:3px solid #1a56db;padding-left:16px;background:#eff6ff;'>
  <p style='margin:0;font-style:italic;color:#1e40af;'>{commentPreview}</p>
</div>
<a href='{appUrl}/invoices/{invoiceId}' class='btn'>View Comment</a>";

        return ($"{mentionedByName} mentioned you on {invoiceNumber}", Render(body));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// SendGrid Implementation
// ══════════════════════════════════════════════════════════════════════════════

public class SendGridEmailService : IEmailService
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<SendGridEmailService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body)
    {
        var apiKey   = _config["SendGrid:ApiKey"];
        var fromEmail = _config["SendGrid:FromEmail"] ?? "noreply@ocrinvoicesaas.com";
        var fromName  = _config["SendGrid:FromName"]  ?? "OCR Invoice SaaS";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("SendGrid:ApiKey not configured. Email not sent to {To}", to);
            return;
        }

        var payload = new
        {
            personalizations = new[]
            {
                new { to = new[] { new { email = to } }, subject }
            },
            from    = new { email = fromEmail, name = fromName },
            content = new[]
            {
                new { type = "text/html", value = body }
            }
        };

        var client = _http.CreateClient("SendGridClient");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.PostAsync(
            "https://api.sendgrid.com/v3/mail/send",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("SendGrid delivery failed for {To}: {Status} — {Error}",
                to, response.StatusCode, err);
        }
        else
        {
            _logger.LogInformation("Email sent via SendGrid to {To}: {Subject}", to, subject);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Mailgun Implementation
// ══════════════════════════════════════════════════════════════════════════════

public class MailgunEmailService : IEmailService
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MailgunEmailService> _logger;

    public MailgunEmailService(
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<MailgunEmailService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body)
    {
        var apiKey  = _config["Mailgun:ApiKey"];
        var domain  = _config["Mailgun:Domain"];
        var from    = _config["Mailgun:From"] ?? $"noreply@{domain}";
        var region  = _config["Mailgun:Region"] ?? "US"; // US or EU

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(domain))
        {
            _logger.LogWarning("Mailgun not configured. Email not sent to {To}", to);
            return;
        }

        var baseUrl = region.Equals("EU", StringComparison.OrdinalIgnoreCase)
            ? "https://api.eu.mailgun.net"
            : "https://api.mailgun.net";

        var client = _http.CreateClient("MailgunClient");

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("from",    from),
            new KeyValuePair<string, string>("to",      to),
            new KeyValuePair<string, string>("subject", subject),
            new KeyValuePair<string, string>("html",    body)
        });

        var response = await client.PostAsync(
            $"{baseUrl}/v3/{domain}/messages", formData);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Mailgun delivery failed for {To}: {Status} — {Error}",
                to, response.StatusCode, err);
        }
        else
        {
            _logger.LogInformation("Email sent via Mailgun to {To}: {Subject}", to, subject);
        }
    }
}
