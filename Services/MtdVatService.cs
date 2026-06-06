using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class MtdVatService
{
    private readonly ApplicationDbContext _db;
    public MtdVatService(ApplicationDbContext db) => _db = db;

    public async Task<ServiceResult<MtdVatReturnResponse>> CalculateAsync(
        Guid companyId, MtdVatReturnRequest request, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<MtdVatReturnResponse>.Fail("Access denied.", 403);

        var invoices = await _db.Invoices
            .Include(i => i.InvoiceItems)
            .Where(i => i.CompanyId == companyId && i.Status == InvoiceStatus.Approved &&
                        i.InvoiceDate.HasValue && i.InvoiceDate.Value >= request.PeriodStart &&
                        i.InvoiceDate.Value <= request.PeriodEnd)
            .ToListAsync();

        var box4 = Math.Round(invoices.Sum(i => i.TaxAmount ?? 0), 2);
        var box7 = Math.Round(invoices.Sum(i => i.SubTotal ?? (i.TotalAmount - i.TaxAmount) ?? 0), 2);

        var allItems = invoices.SelectMany(i => i.InvoiceItems).ToList();
        var standardRate = allItems.Where(item => (item.TaxRate ?? 20m) == 20m).ToList();
        var reducedRate = allItems.Where(item => item.TaxRate == 5m).ToList();
        var zeroRate = allItems.Where(item => item.TaxRate == 0m).ToList();

        var breakdown = new MtdVatBreakdown
        {
            StandardRateItems = new List<MtdVatLineItem> { new() { Description = "Standard rate (20%)", NetAmount = standardRate.Sum(i => i.LineTotal), VatAmount = standardRate.Sum(i => Math.Round(i.LineTotal * ((i.TaxRate ?? 20m) / 100), 2)), TaxRate = 20m, InvoiceCount = standardRate.Count } },
            ReducedRateItems = new List<MtdVatLineItem> { new() { Description = "Reduced rate (5%)", NetAmount = reducedRate.Sum(i => i.LineTotal), VatAmount = reducedRate.Sum(i => Math.Round(i.LineTotal * 0.05m, 2)), TaxRate = 5m, InvoiceCount = reducedRate.Count } },
            ZeroRateItems = new List<MtdVatLineItem> { new() { Description = "Zero-rated", NetAmount = zeroRate.Sum(i => i.LineTotal), VatAmount = 0m, TaxRate = 0m, InvoiceCount = zeroRate.Count } }
        };

        var warnings = new List<string>();
        if (invoices.Count == 0) warnings.Add("No approved invoices found for this period.");
        if (string.IsNullOrWhiteSpace(request.VatRegistrationNumber)) warnings.Add("VAT registration number not provided.");

        var periodKey = request.PeriodStart.Month switch { 1 or 2 or 3 => "AA", 4 or 5 or 6 => "AB", 7 or 8 or 9 => "AC", _ => "AD" };
        periodKey = $"{(request.PeriodStart.Year % 100):D2}{periodKey}";

        return ServiceResult<MtdVatReturnResponse>.Success(new MtdVatReturnResponse
        {
            CompanyId = companyId, VatRegistrationNumber = request.VatRegistrationNumber,
            PeriodStart = request.PeriodStart, PeriodEnd = request.PeriodEnd, PeriodKey = periodKey,
            VatScheme = request.VatScheme, CalculatedAt = DateTime.UtcNow, InvoiceCount = invoices.Count,
            Box1VatDueOnSales = Math.Round(request.ManualBox1OutputVat, 2),
            Box2VatDueOnAcquisitions = 0m, Box4VatReclaimedOnPurchases = box4,
            Box7TotalPurchasesExcludingVat = box7,
            Box6TotalSalesExcludingVat = Math.Round(request.ManualBox6SalesExcludingVat, 2),
            Box8SuppliesEc = 0m, Box9AcquisitionsEc = 0m,
            IsValid = warnings.Count == 0, ValidationWarnings = warnings, Breakdown = breakdown,
            StatusMessage = warnings.Count == 0 ? "Return data is ready." : $"{warnings.Count} warning(s) found."
        });
    }
}
