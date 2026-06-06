using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;

namespace OcrInvoiceSaaS.Services;

public interface INotificationDispatcher
{
    Task OcrCompletedAsync(Guid invoiceId, bool success);
    Task ApprovalRequiredAsync(Guid instanceId, int stepOrder);
    Task ApprovalCompletedAsync(Guid instanceId, bool approved, string? reason = null);
    Task DuplicateFlaggedAsync(Guid invoiceId, Guid duplicateFlagId);
    Task BulkOcrCompletedAsync(Guid jobId);
    Task InvoiceStatusChangedAsync(Guid invoiceId, InvoiceStatus oldStatus, InvoiceStatus newStatus);
    Task PaymentRecordedAsync(Guid paymentId);
}

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationService _inApp;
    private readonly IEmailService _email;
    private readonly IWebhookService _webhooks;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationDispatcher> _logger;

    private string AppUrl => _config["App:BaseUrl"] ?? "https://app.ocrinvoicesaas.com";

    public NotificationDispatcher(ApplicationDbContext db, INotificationService inApp, IEmailService email, IWebhookService webhooks, IConfiguration config, ILogger<NotificationDispatcher> logger)
    {
        _db = db; _inApp = inApp; _email = email; _webhooks = webhooks; _config = config; _logger = logger;
    }

    public async Task OcrCompletedAsync(Guid invoiceId, bool success)
    {
        var invoice = await LoadInvoiceAsync(invoiceId);
        if (invoice == null) return;
        var status = success ? "Processed" : "Failed";
        await _inApp.CreateAsync(invoice.UploadedByUserId, success ? "OCR extraction complete" : "OCR extraction failed", $"{invoice.FileName} — {(success ? "Data extracted successfully." : "Processing failed.")}", success ? "Success" : "Error");
        try
        {
            var (subject, html) = EmailTemplates.OcrCompleted(invoice.UploadedByUser.FullName, invoice.FileName, invoice.InvoiceNumber ?? "—", status, AppUrl, invoiceId);
            await _email.SendAsync(invoice.UploadedByUser.Email, subject, html);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "OCR email failed for invoice {Id}", invoiceId); }
        await DispatchWebhookSafeAsync(invoice.CompanyId, WebhookEvent.InvoiceOcrCompleted, new { invoiceId, fileName = invoice.FileName, invoiceNumber = invoice.InvoiceNumber, status, success, companyId = invoice.CompanyId });
    }

    public async Task ApprovalRequiredAsync(Guid instanceId, int stepOrder)
    {
        var instance = await _db.InvoiceApprovalInstances
            .Include(a => a.Invoice).ThenInclude(i => i.UploadedByUser)
            .Include(a => a.Invoice).ThenInclude(i => i.Vendor)
            .Include(a => a.WorkflowTemplate).ThenInclude(t => t.Steps).ThenInclude(s => s.AssignedUser)
            .FirstOrDefaultAsync(a => a.Id == instanceId);
        if (instance == null) return;
        var step = instance.WorkflowTemplate?.Steps.FirstOrDefault(s => s.StepOrder == stepOrder);
        if (step?.AssignedUser == null) return;
        var invoice = instance.Invoice;
        var vendorName = invoice.Vendor?.Name ?? invoice.ExtractedVendorName ?? "Unknown";
        var amount = $"{invoice.Currency ?? "GBP"} {invoice.TotalAmount:N2}";
        await _inApp.CreateAsync(step.AssignedUser.Id, "Approval required", $"Step '{step.StepName}' on {invoice.InvoiceNumber ?? invoice.FileName} needs your action.", "Info");
        try
        {
            var (subject, html) = EmailTemplates.ApprovalRequired(step.AssignedUser.FullName, step.StepName, invoice.InvoiceNumber ?? invoice.FileName, vendorName, amount, instanceId, AppUrl);
            await _email.SendAsync(step.AssignedUser.Email, subject, html);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Approval email failed for instance {Id}", instanceId); }
        await DispatchWebhookSafeAsync(invoice.CompanyId, WebhookEvent.ApprovalRequested, new { instanceId, invoiceId = invoice.Id, stepName = step.StepName, stepOrder, assignedTo = step.AssignedUser.Email, companyId = invoice.CompanyId });
    }

    public async Task ApprovalCompletedAsync(Guid instanceId, bool approved, string? reason = null)
    {
        var instance = await _db.InvoiceApprovalInstances
            .Include(a => a.Invoice).ThenInclude(i => i.UploadedByUser)
            .Include(a => a.Invoice).ThenInclude(i => i.Vendor)
            .Include(a => a.WorkflowTemplate)
            .Include(a => a.Actions).ThenInclude(ac => ac.ActedByUser)
            .FirstOrDefaultAsync(a => a.Id == instanceId);
        if (instance == null) return;
        var invoice = instance.Invoice; var uploader = invoice.UploadedByUser;
        var vendorName = invoice.Vendor?.Name ?? invoice.ExtractedVendorName ?? "Unknown";
        var amount = $"{invoice.Currency ?? "GBP"} {invoice.TotalAmount:N2}";
        var lastAction = instance.Actions.OrderByDescending(a => a.ActedAt).FirstOrDefault();
        if (approved)
        {
            await _inApp.CreateAsync(uploader.Id, "Invoice approved", $"{invoice.InvoiceNumber ?? invoice.FileName} has been fully approved.", "Success");
            try { var (subject, html) = EmailTemplates.InvoiceApproved(uploader.FullName, invoice.InvoiceNumber ?? invoice.FileName, vendorName, amount, invoice.Id, AppUrl); await _email.SendAsync(uploader.Email, subject, html); }
            catch (Exception ex) { _logger.LogWarning(ex, "Approval-complete email failed for invoice {Id}", invoice.Id); }
            await DispatchWebhookSafeAsync(invoice.CompanyId, WebhookEvent.InvoiceApproved, new { instanceId, invoiceId = invoice.Id, invoiceNumber = invoice.InvoiceNumber, approvedBy = lastAction?.ActedByUser?.Email, companyId = invoice.CompanyId });
        }
        else
        {
            var stepName = lastAction?.StepName ?? "Unknown step";
            await _inApp.CreateAsync(uploader.Id, "Invoice rejected", $"{invoice.InvoiceNumber ?? invoice.FileName} was rejected at '{stepName}'.", "Error");
            try { var (subject, html) = EmailTemplates.InvoiceRejected(uploader.FullName, invoice.InvoiceNumber ?? invoice.FileName, stepName, reason ?? "No reason provided.", invoice.Id, AppUrl); await _email.SendAsync(uploader.Email, subject, html); }
            catch (Exception ex) { _logger.LogWarning(ex, "Rejection email failed for invoice {Id}", invoice.Id); }
            await DispatchWebhookSafeAsync(invoice.CompanyId, WebhookEvent.InvoiceRejected, new { instanceId, invoiceId = invoice.Id, invoiceNumber = invoice.InvoiceNumber, rejectedBy = lastAction?.ActedByUser?.Email, reason, companyId = invoice.CompanyId });
        }
    }

    public async Task DuplicateFlaggedAsync(Guid invoiceId, Guid duplicateFlagId)
    {
        var invoice = await LoadInvoiceAsync(invoiceId);
        if (invoice == null) return;
        var flag = await _db.InvoiceDuplicates.FindAsync(duplicateFlagId);
        if (flag == null) return;
        var adminUsers = await _db.CompanyUsers.Include(cu => cu.User).Where(cu => cu.CompanyId == invoice.CompanyId && (cu.Role == "Owner" || cu.Role == "Admin")).ToListAsync();
        foreach (var cu in adminUsers)
        {
            await _inApp.CreateAsync(cu.UserId, "Duplicate invoice detected", $"{invoice.FileName} may be a duplicate ({flag.MatchScore:0}% confidence).", "Warning");
            try { var (subject, html) = EmailTemplates.DuplicateDetected(cu.User.FullName, invoice.InvoiceNumber ?? "—", invoice.FileName, flag.MatchReason, flag.MatchScore, invoiceId, AppUrl); await _email.SendAsync(cu.User.Email, subject, html); }
            catch (Exception ex) { _logger.LogWarning(ex, "Duplicate flag email failed for invoice {Id}", invoiceId); }
        }
        await DispatchWebhookSafeAsync(invoice.CompanyId, WebhookEvent.DuplicateFlagged, new { invoiceId, duplicateFlagId, matchReason = flag.MatchReason, matchScore = flag.MatchScore, companyId = invoice.CompanyId });
    }

    public async Task BulkOcrCompletedAsync(Guid jobId)
    {
        var job = await _db.BulkOcrJobs.Include(j => j.CreatedByUser).Include(j => j.Company).FirstOrDefaultAsync(j => j.Id == jobId);
        if (job == null) return;
        try { var (subject, html) = EmailTemplates.BulkOcrComplete(job.CreatedByUser.FullName, job.TotalItems, job.ProcessedItems, job.FailedItems, jobId, AppUrl); await _email.SendAsync(job.CreatedByUser.Email, subject, html); }
        catch (Exception ex) { _logger.LogWarning(ex, "Bulk OCR email failed for job {Id}", jobId); }
        await DispatchWebhookSafeAsync(job.CompanyId, WebhookEvent.BulkOcrCompleted, new { jobId, total = job.TotalItems, processed = job.ProcessedItems, failed = job.FailedItems, companyId = job.CompanyId });
    }

    public async Task InvoiceStatusChangedAsync(Guid invoiceId, InvoiceStatus oldStatus, InvoiceStatus newStatus)
    {
        var invoice = await LoadInvoiceAsync(invoiceId);
        if (invoice == null) return;
        await DispatchWebhookSafeAsync(invoice.CompanyId, WebhookEvent.InvoiceStatusChanged, new { invoiceId, invoiceNumber = invoice.InvoiceNumber, oldStatus = oldStatus.ToString(), newStatus = newStatus.ToString(), changedAt = DateTime.UtcNow, companyId = invoice.CompanyId });
    }

    public async Task PaymentRecordedAsync(Guid paymentId)
    {
        var payment = await _db.Payments.Include(p => p.CompanySubscription).ThenInclude(cs => cs.Company).FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment == null) return;
        await DispatchWebhookSafeAsync(payment.CompanySubscription.CompanyId, WebhookEvent.PaymentRecorded, new { paymentId, amount = payment.Amount, currency = payment.Currency, status = payment.Status, companyId = payment.CompanySubscription.CompanyId });
    }

    private async Task<Invoice?> LoadInvoiceAsync(Guid invoiceId)
        => await _db.Invoices.Include(i => i.UploadedByUser).Include(i => i.Vendor).Include(i => i.Company).FirstOrDefaultAsync(i => i.Id == invoiceId);

    private async Task DispatchWebhookSafeAsync(Guid companyId, WebhookEvent evt, object payload)
    {
        try { await _webhooks.DispatchEventAsync(companyId, evt, payload); }
        catch (Exception ex) { _logger.LogWarning(ex, "Webhook dispatch failed for event {Event}", evt); }
    }
}
