using AutoMapper;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class InvoiceService : IInvoiceService
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public InvoiceService(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }

    private async Task<bool> UserBelongsToCompanyAsync(Guid companyId, Guid userId)
        => await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);

    public async Task<ServiceResult<InvoiceResponse>> CreateInvoiceAsync(Guid companyId, CreateInvoiceRequest request, Guid userId)
    {
        if (!await UserBelongsToCompanyAsync(companyId, userId))
            return ServiceResult<InvoiceResponse>.Fail("Access denied.", 403);

        var invoice = new Invoice
        {
            CompanyId = companyId,
            UploadedByUserId = userId,
            FileName = request.FileName.Trim(),
            FileUrl = request.FileUrl.Trim(),
            FileType = request.FileType.Trim().ToLower(),
            VendorId = request.VendorId,
            ExpenseCategoryId = request.ExpenseCategoryId,
            Notes = request.Notes?.Trim(),
            Status = InvoiceStatus.Uploaded
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return await GetInvoiceByIdAsync(invoice.Id, userId);
    }

    public async Task<ServiceResult<List<InvoiceListResponse>>> GetInvoicesAsync(Guid companyId, Guid userId)
    {
        if (!await UserBelongsToCompanyAsync(companyId, userId))
            return ServiceResult<List<InvoiceListResponse>>.Fail("Access denied.", 403);

        var invoices = await _db.Invoices
            .Include(i => i.Vendor)
            .Where(i => i.CompanyId == companyId)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvoiceListResponse
            {
                Id = i.Id,
                FileName = i.FileName,
                InvoiceNumber = i.InvoiceNumber,
                TotalAmount = i.TotalAmount,
                Currency = i.Currency,
                Status = i.Status.ToString(),
                VendorName = i.Vendor != null ? i.Vendor.Name : i.ExtractedVendorName,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<InvoiceListResponse>>.Success(invoices);
    }

    public async Task<ServiceResult<InvoiceResponse>> GetInvoiceByIdAsync(Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Vendor)
            .Include(i => i.UploadedByUser)
            .Include(i => i.InvoiceItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<InvoiceResponse>.Fail("Invoice not found.", 404);
        if (!await UserBelongsToCompanyAsync(invoice.CompanyId, userId))
            return ServiceResult<InvoiceResponse>.Fail("Access denied.", 403);

        return ServiceResult<InvoiceResponse>.Success(_mapper.Map<InvoiceResponse>(invoice));
    }

    public async Task<ServiceResult<InvoiceResponse>> UpdateInvoiceAsync(Guid invoiceId, UpdateInvoiceRequest request, Guid userId)
    {
        var invoice = await _db.Invoices.FindAsync(invoiceId);
        if (invoice == null) return ServiceResult<InvoiceResponse>.Fail("Invoice not found.", 404);
        if (!await UserBelongsToCompanyAsync(invoice.CompanyId, userId))
            return ServiceResult<InvoiceResponse>.Fail("Access denied.", 403);

        if (request.InvoiceNumber != null) invoice.InvoiceNumber = request.InvoiceNumber.Trim();
        if (request.InvoiceDate.HasValue) invoice.InvoiceDate = request.InvoiceDate;
        if (request.DueDate.HasValue) invoice.DueDate = request.DueDate;
        if (request.TotalAmount.HasValue) invoice.TotalAmount = request.TotalAmount;
        if (request.TaxAmount.HasValue) invoice.TaxAmount = request.TaxAmount;
        if (request.SubTotal.HasValue) invoice.SubTotal = request.SubTotal;
        if (request.Currency != null) invoice.Currency = request.Currency.Trim().ToUpper();
        if (request.VendorId.HasValue) invoice.VendorId = request.VendorId;
        if (request.ExpenseCategoryId.HasValue) invoice.ExpenseCategoryId = request.ExpenseCategoryId;
        if (request.Notes != null) invoice.Notes = request.Notes.Trim();
        if (request.Status.HasValue) invoice.Status = request.Status.Value;

        invoice.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await GetInvoiceByIdAsync(invoiceId, userId);
    }
}
