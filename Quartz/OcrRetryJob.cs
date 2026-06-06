using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Models;
using Quartz;

namespace OcrInvoiceSaaS.Quartz;

[DisallowConcurrentExecution]
public class OcrRetryJob : IJob
{
    private readonly ApplicationDbContext _db;
    private readonly IOcrProvider _ocrProvider;
    private readonly ILogger<OcrRetryJob> _logger;

    public OcrRetryJob(ApplicationDbContext db, IOcrProvider ocrProvider, ILogger<OcrRetryJob> logger)
    {
        _db = db; _ocrProvider = ocrProvider; _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("OcrRetryJob started at {Time}", DateTime.UtcNow);
        var cutoff = DateTime.UtcNow.AddMinutes(-10);

        var stuckInvoices = await _db.Invoices
            .Where(i => i.Status == InvoiceStatus.Failed || (i.Status == InvoiceStatus.Processing && i.UpdatedAt < cutoff))
            .Take(20).ToListAsync();

        foreach (var invoice in stuckInvoices)
        {
            try
            {
                invoice.Status = InvoiceStatus.Processing; invoice.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                var extracted = await _ocrProvider.ExtractAsync(invoice.FileUrl, invoice.FileType);
                invoice.RawOcrText = extracted.RawText;
                invoice.InvoiceNumber ??= extracted.InvoiceNumber;
                invoice.InvoiceDate ??= extracted.InvoiceDate;
                invoice.TotalAmount ??= extracted.TotalAmount;
                invoice.ExtractedVendorName ??= extracted.VendorName;
                invoice.Status = InvoiceStatus.Processed; invoice.UpdatedAt = DateTime.UtcNow;
                _db.OcrProcessingLogs.Add(new OcrProcessingLog { InvoiceId = invoice.Id, Provider = _ocrProvider.ProviderName, Success = true, DurationMs = 0 });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                invoice.Status = InvoiceStatus.Failed; invoice.UpdatedAt = DateTime.UtcNow;
                _db.OcrProcessingLogs.Add(new OcrProcessingLog { InvoiceId = invoice.Id, Provider = _ocrProvider.ProviderName, Success = false, ErrorMessage = ex.Message });
                await _db.SaveChangesAsync();
                _logger.LogWarning("Retry failed for invoice {Id}: {Error}", invoice.Id, ex.Message);
            }
        }
    }
}
