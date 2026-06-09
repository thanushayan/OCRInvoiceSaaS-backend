using AutoMapper;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class InvoiceItemService : IInvoiceItemService
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public InvoiceItemService(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    private async Task<(Invoice? invoice, bool hasAccess)> LoadInvoiceWithAccessCheckAsync(
        Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return (null, false);

        bool hasAccess = invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId);
        return (invoice, hasAccess);
    }

    public async Task<ServiceResult<InvoiceItemResponse>> AddItemAsync(
        Guid invoiceId, AddInvoiceItemRequest request, Guid userId)
    {
        var (invoice, hasAccess) = await LoadInvoiceWithAccessCheckAsync(invoiceId, userId);

        if (invoice == null)
            return ServiceResult<InvoiceItemResponse>.Fail("Invoice not found.", 404);
        if (!hasAccess)
            return ServiceResult<InvoiceItemResponse>.Fail("Access denied.", 403);

        var lineTotal = Math.Round(request.Quantity * request.UnitPrice, 2);

        var item = new InvoiceItem
        {
            InvoiceId = invoiceId,
            Description = request.Description.Trim(),
            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice,
            LineTotal = lineTotal,
            TaxRate = request.TaxRate
        };

        _db.InvoiceItems.Add(item);

        // Recalculate invoice totals
        await RecalculateInvoiceTotalsAsync(invoice.Id);

        await _db.SaveChangesAsync();

        return ServiceResult<InvoiceItemResponse>.Success(_mapper.Map<InvoiceItemResponse>(item), 201);
    }

    public async Task<ServiceResult<InvoiceItemResponse>> UpdateItemAsync(
        Guid itemId, UpdateInvoiceItemRequest request, Guid userId)
    {
        var item = await _db.InvoiceItems.FindAsync(itemId);
        if (item == null)
            return ServiceResult<InvoiceItemResponse>.Fail("Item not found.", 404);

        var (invoice, hasAccess) = await LoadInvoiceWithAccessCheckAsync(item.InvoiceId, userId);
        if (!hasAccess)
            return ServiceResult<InvoiceItemResponse>.Fail("Access denied.", 403);

        if (request.Description != null) item.Description = request.Description.Trim();
        if (request.Quantity.HasValue) item.Quantity = request.Quantity.Value;
        if (request.UnitPrice.HasValue) item.UnitPrice = request.UnitPrice.Value;
        if (request.TaxRate.HasValue) item.TaxRate = request.TaxRate.Value;

        item.LineTotal = Math.Round(item.Quantity * item.UnitPrice, 2);

        await RecalculateInvoiceTotalsAsync(item.InvoiceId);
        await _db.SaveChangesAsync();

        return ServiceResult<InvoiceItemResponse>.Success(_mapper.Map<InvoiceItemResponse>(item));
    }

    public async Task<ServiceResult> DeleteItemAsync(Guid itemId, Guid userId)
    {
        var item = await _db.InvoiceItems.FindAsync(itemId);
        if (item == null)
            return ServiceResult.Fail("Item not found.", 404);

        var (invoice, hasAccess) = await LoadInvoiceWithAccessCheckAsync(item.InvoiceId, userId);
        if (!hasAccess)
            return ServiceResult.Fail("Access denied.", 403);

        _db.InvoiceItems.Remove(item);
        await RecalculateInvoiceTotalsAsync(item.InvoiceId);
        await _db.SaveChangesAsync();

        return ServiceResult.Success(204);
    }

    // Recalculates SubTotal, TaxAmount, TotalAmount from line items
    private async Task RecalculateInvoiceTotalsAsync(Guid invoiceId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.InvoiceItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return;

        var items = invoice.InvoiceItems.ToList();
        invoice.SubTotal = items.Sum(i => i.LineTotal);
        invoice.TaxAmount = items.Sum(i => i.TaxRate.HasValue
            ? Math.Round(i.LineTotal * (i.TaxRate.Value / 100), 2)
            : 0);
        invoice.TotalAmount = invoice.SubTotal + invoice.TaxAmount;
        invoice.UpdatedAt = DateTime.UtcNow;
    }
}
