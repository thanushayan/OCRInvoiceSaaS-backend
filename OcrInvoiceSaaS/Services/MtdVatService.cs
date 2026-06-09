using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

/// <summary>
/// Calculates UK MTD VAT return boxes 1–9 from invoice data.
///
/// Note: This system tracks PURCHASE invoices (input tax — Box 4 & 7).
/// Box 1 (output VAT on sales) and Box 6 (net sales) require manual input
/// from the company's sales ledger and are supplied in the request.
///
/// HMRC MTD API reference: https://developer.service.hmrc.gov.uk/api-documentation/docs/api/service/vat-api
/// </summary>
public class MtdVatService
{
    private readonly ApplicationDbContext _db;

    public MtdVatService(ApplicationDbContext db) => _db = db;

    public async Task<ServiceResult<MtdVatReturnResponse>> CalculateAsync(
        Guid companyId, MtdVatReturnRequest request, Guid userId)
    {
        bool isMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<MtdVatReturnResponse>.Fail("Access denied.", 403);

        // Pull all approved invoices with items in the VAT period
        var invoices = await _db.Invoices
            .Include(i => i.InvoiceItems)
            .Include(i => i.Vendor)
            .Where(i =>
                i.CompanyId == companyId &&
                i.Status == InvoiceStatus.Approved &&
                i.InvoiceDate.HasValue &&
                i.InvoiceDate.Value >= request.PeriodStart &&
                i.InvoiceDate.Value <= request.PeriodEnd)
            .ToListAsync();

        // ── Box 4: VAT reclaimed on purchases ────────────────────────────────
        // Sum of all tax amounts on purchase invoices — this is the input tax we reclaim
        var box4 = invoices.Sum(i => i.TaxAmount ?? 0);
        var box4Rounded = Math.Round(box4, 2);

        // ── Box 7: Total purchases excluding VAT ─────────────────────────────
        var box7 = invoices.Sum(i => i.SubTotal ?? (i.TotalAmount - i.TaxAmount) ?? 0);
        var box7Rounded = Math.Round(box7, 2);

        // ── VAT breakdown by rate ─────────────────────────────────────────────
        var allItems = invoices.SelectMany(i => i.InvoiceItems).ToList();

        var standardRate = allItems.Where(item => (item.TaxRate ?? 20m) == 20m).ToList();
        var reducedRate  = allItems.Where(item => item.TaxRate == 5m).ToList();
        var zeroRate     = allItems.Where(item => item.TaxRate == 0m).ToList();

        var breakdown = new MtdVatBreakdown
        {
            StandardRateItems = new List<MtdVatLineItem>
            {
                new()
                {
                    Description  = "Standard rate purchases (20%)",
                    NetAmount    = standardRate.Sum(i => i.LineTotal),
                    VatAmount    = standardRate.Sum(i => Math.Round(i.LineTotal * ((i.TaxRate ?? 20m) / 100), 2)),
                    TaxRate      = 20m,
                    InvoiceCount = standardRate.Count
                }
            },
            ReducedRateItems = new List<MtdVatLineItem>
            {
                new()
                {
                    Description  = "Reduced rate purchases (5%)",
                    NetAmount    = reducedRate.Sum(i => i.LineTotal),
                    VatAmount    = reducedRate.Sum(i => Math.Round(i.LineTotal * 0.05m, 2)),
                    TaxRate      = 5m,
                    InvoiceCount = reducedRate.Count
                }
            },
            ZeroRateItems = new List<MtdVatLineItem>
            {
                new()
                {
                    Description  = "Zero-rated / exempt purchases",
                    NetAmount    = zeroRate.Sum(i => i.LineTotal),
                    VatAmount    = 0m,
                    TaxRate      = 0m,
                    InvoiceCount = zeroRate.Count
                }
            }
        };

        // ── Validation warnings ────────────────────────────────────────────────
        var warnings = new List<string>();

        if (invoices.Count == 0)
            warnings.Add("No approved invoices found for this period. Ensure invoices are approved before submitting.");

        if (string.IsNullOrWhiteSpace(request.VatRegistrationNumber))
            warnings.Add("VAT registration number not provided. Required for HMRC submission.");

        if (request.ManualBox6SalesExcludingVat == 0)
            warnings.Add("Box 6 (net sales) is zero. If your business has sales, enter them in the ManualBox6SalesExcludingVat field.");

        if (request.ManualBox1OutputVat == 0 && request.ManualBox6SalesExcludingVat > 0)
            warnings.Add("Box 6 contains sales but Box 1 (output VAT) is zero. Verify whether sales are VAT-exempt or zero-rated.");

        // Cross-check: Box 4 should not exceed Box 3 significantly
        if (box4Rounded > request.ManualBox1OutputVat * 2 && request.ManualBox1OutputVat > 0)
            warnings.Add("Input tax (Box 4) significantly exceeds output tax (Box 1). This will trigger a VAT repayment — HMRC may investigate.");

        // ── Build period key (HMRC format: YY + quarter letter) ───────────────
        var periodKey = BuildPeriodKey(request.PeriodStart, request.PeriodEnd);

        var response = new MtdVatReturnResponse
        {
            CompanyId                   = companyId,
            VatRegistrationNumber       = request.VatRegistrationNumber,
            PeriodStart                 = request.PeriodStart,
            PeriodEnd                   = request.PeriodEnd,
            PeriodKey                   = periodKey,
            VatScheme                   = request.VatScheme,
            CalculatedAt                = DateTime.UtcNow,
            InvoiceCount                = invoices.Count,

            // Output side — supplied manually (we hold purchase invoices, not sales)
            Box1VatDueOnSales           = Math.Round(request.ManualBox1OutputVat, 2),
            Box2VatDueOnAcquisitions    = 0m,    // post-Brexit = 0 for most UK businesses

            // Input side — calculated from purchase invoices
            Box4VatReclaimedOnPurchases = box4Rounded,
            Box7TotalPurchasesExcludingVat = box7Rounded,

            // Sales side — supplied manually
            Box6TotalSalesExcludingVat  = Math.Round(request.ManualBox6SalesExcludingVat, 2),

            // Post-Brexit EC figures = 0 for most UK businesses
            Box8SuppliesEc              = 0m,
            Box9AcquisitionsEc          = 0m,

            IsValid                     = warnings.Count == 0,
            ValidationWarnings          = warnings,
            Breakdown                   = breakdown,
            StatusMessage               = warnings.Count == 0
                ? "Return data is ready for HMRC submission."
                : $"{warnings.Count} warning(s) found. Review before submitting."
        };

        return ServiceResult<MtdVatReturnResponse>.Success(response);
    }

    /// <summary>
    /// Builds the HMRC period key format (e.g. "24AA" = Q1 2024, "24AB" = Q2 2024).
    /// Standard quarterly periods: AA=Jan-Mar, AB=Apr-Jun, AC=Jul-Sep, AD=Oct-Dec.
    /// </summary>
    private static string BuildPeriodKey(DateTime start, DateTime end)
    {
        var year   = (start.Year % 100).ToString("D2");
        var quarter = start.Month switch
        {
            1 or 2 or 3   => "AA",
            4 or 5 or 6   => "AB",
            7 or 8 or 9   => "AC",
            10 or 11 or 12 => "AD",
            _ => "AA"
        };

        // Monthly filers use different codes (23#001 format)
        var periodDays = (end - start).TotalDays;
        if (periodDays < 45)
        {
            var month = start.Month.ToString("D2");
            return $"{year}#{month}";
        }

        return $"{year}{quarter}";
    }
}
