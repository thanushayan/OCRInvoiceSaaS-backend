using OcrInvoiceSaaS.Libs;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class OcrService : IOcrService
{
    private readonly ApplicationDbContext _db;
    private readonly IOcrProvider _ocrProvider;
    private readonly INotificationDispatcher _dispatcher;

    public OcrService(ApplicationDbContext db, IOcrProvider ocrProvider, INotificationDispatcher dispatcher)
    {
        _db         = db;
        _ocrProvider = ocrProvider;
        _dispatcher  = dispatcher;
    }

    public async Task<ServiceResult<OcrResultResponse>> ProcessInvoiceAsync(Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
            return ServiceResult<OcrResultResponse>.Fail("Invoice not found.", 404);

        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<OcrResultResponse>.Fail("Access denied.", 403);

        invoice.Status    = InvoiceStatus.Processing;
        invoice.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var stopwatch = Stopwatch.StartNew();
        OcrResultResponse result;
        bool success = false;

        try
        {
            var extracted = await _ocrProvider.ExtractAsync(invoice.FileUrl, invoice.FileType);
            stopwatch.Stop();

            invoice.RawOcrText         = extracted.RawText;
            invoice.InvoiceNumber      = extracted.InvoiceNumber;
            invoice.InvoiceDate        = extracted.InvoiceDate;
            invoice.TotalAmount        = extracted.TotalAmount;
            invoice.ExtractedVendorName = extracted.VendorName;
            invoice.Status             = InvoiceStatus.Processed;
            invoice.UpdatedAt          = DateTime.UtcNow;

            _db.OcrProcessingLogs.Add(new OcrProcessingLog
            {
                InvoiceId   = invoiceId,
                Provider    = _ocrProvider.ProviderName,
                Success     = true,
                DurationMs  = (int)stopwatch.ElapsedMilliseconds,
                RawResponse = JsonSerializer.Serialize(extracted)
            });

            await _db.SaveChangesAsync();
            success = true;

            result = new OcrResultResponse
            {
                RawText       = extracted.RawText,
                InvoiceNumber = extracted.InvoiceNumber,
                InvoiceDate   = extracted.InvoiceDate,
                TotalAmount   = extracted.TotalAmount,
                VendorName    = extracted.VendorName,
                Provider      = _ocrProvider.ProviderName,
                Success       = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            invoice.Status    = InvoiceStatus.Failed;
            invoice.UpdatedAt = DateTime.UtcNow;

            _db.OcrProcessingLogs.Add(new OcrProcessingLog
            {
                InvoiceId    = invoiceId,
                Provider     = _ocrProvider.ProviderName,
                Success      = false,
                ErrorMessage = ex.Message,
                DurationMs   = (int)stopwatch.ElapsedMilliseconds
            });

            await _db.SaveChangesAsync();

            // Fire notifications for failure
            await _dispatcher.OcrCompletedAsync(invoiceId, false);
            return ServiceResult<OcrResultResponse>.Fail($"OCR processing failed: {ex.Message}", 500);
        }

        // Fire notifications for success
        await _dispatcher.OcrCompletedAsync(invoiceId, success);
        await _dispatcher.InvoiceStatusChangedAsync(invoiceId, InvoiceStatus.Processing, InvoiceStatus.Processed);

        return ServiceResult<OcrResultResponse>.Success(result);
    }
}
