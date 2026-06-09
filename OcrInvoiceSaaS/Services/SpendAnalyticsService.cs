using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class SpendAnalyticsService
{
    private readonly ApplicationDbContext _db;

    public SpendAnalyticsService(ApplicationDbContext db) => _db = db;

    // ── Vendor Drill-Down ─────────────────────────────────────────────────────

    public async Task<ServiceResult<VendorDrilldownResponse>> GetVendorDrilldownAsync(
        Guid companyId, Guid vendorId, ReportFilterRequest filter, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<VendorDrilldownResponse>.Fail("Access denied.", 403);

        var vendor = await _db.Vendors.FindAsync(vendorId);
        if (vendor == null || vendor.CompanyId != companyId)
            return ServiceResult<VendorDrilldownResponse>.Fail("Vendor not found.", 404);

        var invoices = await ApplyFilter(
            _db.Invoices.Where(i => i.CompanyId == companyId && i.VendorId == vendorId),
            filter)
            .Include(i => i.ExpenseCategory)
            .ToListAsync();

        if (invoices.Count == 0)
        {
            return ServiceResult<VendorDrilldownResponse>.Success(new VendorDrilldownResponse
            {
                VendorId   = vendorId,
                VendorName = vendor.Name
            });
        }

        var amounts = invoices.Select(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0).ToList();

        var monthlyTrend = invoices
            .Where(i => i.InvoiceDate.HasValue)
            .GroupBy(i => new { i.InvoiceDate!.Value.Year, i.InvoiceDate.Value.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new SpendByMonth
            {
                Year         = g.Key.Year,
                Month        = g.Key.Month,
                MonthLabel   = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                InvoiceCount = g.Count(),
                TotalAmount  = g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                TaxAmount    = g.Sum(i => i.TaxAmount ?? 0)
            }).ToList();

        var recent = invoices
            .OrderByDescending(i => i.InvoiceDate ?? i.CreatedAt)
            .Take(10)
            .Select(i => new InvoiceListResponse
            {
                Id            = i.Id,
                FileName      = i.FileName,
                InvoiceNumber = i.InvoiceNumber,
                TotalAmount   = i.TotalAmount,
                Currency      = i.Currency,
                Status        = i.Status.ToString(),
                VendorName    = vendor.Name,
                CreatedAt     = i.CreatedAt
            }).ToList();

        return ServiceResult<VendorDrilldownResponse>.Success(new VendorDrilldownResponse
        {
            VendorId            = vendorId,
            VendorName          = vendor.Name,
            VendorEmail         = vendor.ContactEmail,
            VatNumber           = vendor.VatNumber,
            TotalInvoices       = invoices.Count,
            TotalSpend          = amounts.Sum(),
            TotalTax            = invoices.Sum(i => i.TaxAmount ?? 0),
            AverageInvoiceValue = amounts.Average(),
            LargestInvoice      = amounts.Max(),
            SmallestInvoice     = amounts.Min(),
            FirstInvoiceDate    = invoices.Min(i => i.InvoiceDate),
            LastInvoiceDate     = invoices.Max(i => i.InvoiceDate),
            MonthlyTrend        = monthlyTrend,
            RecentInvoices      = recent
        });
    }

    // ── Category Drill-Down ───────────────────────────────────────────────────

    public async Task<ServiceResult<CategoryDrilldownResponse>> GetCategoryDrilldownAsync(
        Guid companyId, Guid categoryId, ReportFilterRequest filter, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<CategoryDrilldownResponse>.Fail("Access denied.", 403);

        var category = await _db.ExpenseCategories.FindAsync(categoryId);
        if (category == null || category.CompanyId != companyId)
            return ServiceResult<CategoryDrilldownResponse>.Fail("Category not found.", 404);

        var invoices = await ApplyFilter(
            _db.Invoices.Where(i => i.CompanyId == companyId && i.ExpenseCategoryId == categoryId),
            filter)
            .Include(i => i.Vendor)
            .ToListAsync();

        // Total company spend for percentage calc
        var totalCompanySpend = await ApplyFilter(
            _db.Invoices.Where(i => i.CompanyId == companyId), filter)
            .SumAsync(i => (i.BaseCurrencyAmount ?? i.TotalAmount) ?? 0);

        var categorySpend = invoices.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0);

        var topVendors = invoices
            .GroupBy(i => new
            {
                VendorId = i.VendorId,
                Name     = i.Vendor?.Name ?? i.ExtractedVendorName ?? "Unknown"
            })
            .Select(g => new SpendByVendor
            {
                VendorId     = g.Key.VendorId,
                VendorName   = g.Key.Name,
                InvoiceCount = g.Count(),
                TotalAmount  = g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                Percentage   = categorySpend > 0
                    ? Math.Round(g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0) / categorySpend * 100, 1)
                    : 0
            })
            .OrderByDescending(v => v.TotalAmount)
            .Take(10)
            .ToList();

        var monthlyTrend = invoices
            .Where(i => i.InvoiceDate.HasValue)
            .GroupBy(i => new { i.InvoiceDate!.Value.Year, i.InvoiceDate.Value.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new SpendByMonth
            {
                Year         = g.Key.Year,
                Month        = g.Key.Month,
                MonthLabel   = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                InvoiceCount = g.Count(),
                TotalAmount  = g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                TaxAmount    = g.Sum(i => i.TaxAmount ?? 0)
            }).ToList();

        return ServiceResult<CategoryDrilldownResponse>.Success(new CategoryDrilldownResponse
        {
            CategoryId               = categoryId,
            CategoryName             = category.Name,
            TotalInvoices            = invoices.Count,
            TotalSpend               = categorySpend,
            TotalTax                 = invoices.Sum(i => i.TaxAmount ?? 0),
            PercentageOfTotalSpend   = totalCompanySpend > 0
                ? Math.Round(categorySpend / totalCompanySpend * 100, 1) : 0,
            AverageInvoiceValue      = invoices.Count > 0 ? categorySpend / invoices.Count : 0,
            TopVendors               = topVendors,
            MonthlyTrend             = monthlyTrend
        });
    }

    // ── Spend Trend ───────────────────────────────────────────────────────────

    public async Task<ServiceResult<SpendTrendResponse>> GetSpendTrendAsync(
        Guid companyId, SpendTrendRequest request, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<SpendTrendResponse>.Fail("Access denied.", 403);

        var filter = new ReportFilterRequest
        {
            From       = request.From,
            To         = request.To,
            Currency   = request.Currency,
            VendorId   = request.VendorId,
            CategoryId = request.CategoryId
        };

        var invoices = await ApplyFilter(
            _db.Invoices.Where(i => i.CompanyId == companyId), filter)
            .ToListAsync();

        var dataPoints = BuildDataPoints(invoices, request.From, request.To, request.Granularity);

        List<TrendDataPoint>? prevPoints = null;
        decimal growthPct = 0;

        if (request.CompareWithPreviousPeriod)
        {
            var span         = request.To - request.From;
            var prevFrom     = request.From - span - TimeSpan.FromDays(1);
            var prevTo       = request.From - TimeSpan.FromDays(1);
            var prevFilter   = new ReportFilterRequest { From = prevFrom, To = prevTo,
                Currency = request.Currency, VendorId = request.VendorId, CategoryId = request.CategoryId };
            var prevInvoices = await ApplyFilter(_db.Invoices.Where(i => i.CompanyId == companyId), prevFilter)
                .ToListAsync();

            prevPoints = BuildDataPoints(prevInvoices, prevFrom, prevTo, request.Granularity);

            var currTotal = invoices.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0);
            var prevTotal = prevInvoices.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0);
            growthPct = prevTotal > 0 ? Math.Round((currTotal - prevTotal) / prevTotal * 100, 1) : 0;
        }

        // Annotate change vs previous data point
        for (int i = 1; i < dataPoints.Count; i++)
        {
            var prev = dataPoints[i - 1].Amount;
            if (prev > 0)
            {
                dataPoints[i].ChangeFromPrevious = dataPoints[i].Amount - prev;
                dataPoints[i].ChangePercent = Math.Round((dataPoints[i].Amount - prev) / prev * 100, 1);
            }
        }

        var totalSpend = invoices.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0);

        return ServiceResult<SpendTrendResponse>.Success(new SpendTrendResponse
        {
            Granularity               = request.Granularity,
            From                      = request.From,
            To                        = request.To,
            TotalSpend                = totalSpend,
            TotalInvoices             = invoices.Count,
            AveragePeriodSpend        = dataPoints.Count > 0 ? totalSpend / dataPoints.Count : 0,
            GrowthVsPreviousPeriod    = growthPct,
            DataPoints                = dataPoints,
            PreviousPeriodDataPoints  = prevPoints
        });
    }

    // ── Top Vendors ───────────────────────────────────────────────────────────

    public async Task<ServiceResult<List<SpendByVendor>>> GetTopVendorsAsync(
        Guid companyId, TopVendorsRequest request, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<List<SpendByVendor>>.Fail("Access denied.", 403);

        var filter = new ReportFilterRequest { From = request.From, To = request.To, Currency = request.Currency };

        var invoices = await ApplyFilter(_db.Invoices.Include(i => i.Vendor)
            .Where(i => i.CompanyId == companyId), filter).ToListAsync();

        var totalSpend = invoices.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0);

        var topVendors = invoices
            .GroupBy(i => new { VendorId = i.VendorId, Name = i.Vendor?.Name ?? i.ExtractedVendorName ?? "Unknown" })
            .Select(g => new SpendByVendor
            {
                VendorId     = g.Key.VendorId,
                VendorName   = g.Key.Name,
                InvoiceCount = g.Count(),
                TotalAmount  = g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                Percentage   = totalSpend > 0
                    ? Math.Round(g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0) / totalSpend * 100, 1) : 0
            })
            .OrderByDescending(v => v.TotalAmount)
            .Take(request.Top)
            .ToList();

        return ServiceResult<List<SpendByVendor>>.Success(topVendors);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IQueryable<Invoice> ApplyFilter(IQueryable<Invoice> query, ReportFilterRequest filter)
    {
        if (filter.From.HasValue)     query = query.Where(i => i.InvoiceDate >= filter.From);
        if (filter.To.HasValue)       query = query.Where(i => i.InvoiceDate <= filter.To);
        if (!string.IsNullOrWhiteSpace(filter.Currency))
            query = query.Where(i => i.Currency == filter.Currency.ToUpper());
        if (filter.VendorId.HasValue) query = query.Where(i => i.VendorId == filter.VendorId);
        if (filter.CategoryId.HasValue) query = query.Where(i => i.ExpenseCategoryId == filter.CategoryId);
        if (!string.IsNullOrWhiteSpace(filter.Status) &&
            Enum.TryParse<InvoiceStatus>(filter.Status, true, out var status))
            query = query.Where(i => i.Status == status);
        return query;
    }

    private static List<TrendDataPoint> BuildDataPoints(
        List<Invoice> invoices, DateTime from, DateTime to, string granularity)
    {
        return granularity.ToLower() switch
        {
            "daily" => BuildDailyPoints(invoices, from, to),
            "weekly" => BuildWeeklyPoints(invoices, from, to),
            "quarterly" => BuildQuarterlyPoints(invoices, from, to),
            _ => BuildMonthlyPoints(invoices, from, to)
        };
    }

    private static List<TrendDataPoint> BuildMonthlyPoints(List<Invoice> invoices, DateTime from, DateTime to)
    {
        var points = new List<TrendDataPoint>();
        var current = new DateTime(from.Year, from.Month, 1);

        while (current <= to)
        {
            var end  = current.AddMonths(1).AddDays(-1);
            var slab = invoices.Where(i => i.InvoiceDate?.Year == current.Year &&
                                           i.InvoiceDate?.Month == current.Month).ToList();
            points.Add(new TrendDataPoint
            {
                Label        = current.ToString("MMM yyyy"),
                PeriodStart  = current,
                PeriodEnd    = end,
                Amount       = slab.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                TaxAmount    = slab.Sum(i => i.TaxAmount ?? 0),
                InvoiceCount = slab.Count
            });
            current = current.AddMonths(1);
        }

        return points;
    }

    private static List<TrendDataPoint> BuildWeeklyPoints(List<Invoice> invoices, DateTime from, DateTime to)
    {
        var points  = new List<TrendDataPoint>();
        var current = from.Date;
        int week    = 1;

        while (current <= to)
        {
            var end  = current.AddDays(6) > to ? to : current.AddDays(6);
            var slab = invoices.Where(i => i.InvoiceDate?.Date >= current && i.InvoiceDate?.Date <= end).ToList();

            points.Add(new TrendDataPoint
            {
                Label        = $"W{week} {current:dd MMM}",
                PeriodStart  = current,
                PeriodEnd    = end,
                Amount       = slab.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                TaxAmount    = slab.Sum(i => i.TaxAmount ?? 0),
                InvoiceCount = slab.Count
            });

            current = current.AddDays(7);
            week++;
        }

        return points;
    }

    private static List<TrendDataPoint> BuildDailyPoints(List<Invoice> invoices, DateTime from, DateTime to)
    {
        var points  = new List<TrendDataPoint>();
        var current = from.Date;

        while (current <= to.Date)
        {
            var slab = invoices.Where(i => i.InvoiceDate?.Date == current).ToList();
            points.Add(new TrendDataPoint
            {
                Label        = current.ToString("dd MMM"),
                PeriodStart  = current,
                PeriodEnd    = current,
                Amount       = slab.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                TaxAmount    = slab.Sum(i => i.TaxAmount ?? 0),
                InvoiceCount = slab.Count
            });
            current = current.AddDays(1);
        }

        return points;
    }

    private static List<TrendDataPoint> BuildQuarterlyPoints(List<Invoice> invoices, DateTime from, DateTime to)
    {
        var points    = new List<TrendDataPoint>();
        var current   = new DateTime(from.Year, ((from.Month - 1) / 3) * 3 + 1, 1);

        while (current <= to)
        {
            var end  = current.AddMonths(3).AddDays(-1);
            var q    = (current.Month - 1) / 3 + 1;
            var slab = invoices.Where(i => i.InvoiceDate >= current && i.InvoiceDate <= end).ToList();

            points.Add(new TrendDataPoint
            {
                Label        = $"Q{q} {current.Year}",
                PeriodStart  = current,
                PeriodEnd    = end,
                Amount       = slab.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                TaxAmount    = slab.Sum(i => i.TaxAmount ?? 0),
                InvoiceCount = slab.Count
            });

            current = current.AddMonths(3);
        }

        return points;
    }

    private async Task<bool> IsMemberAsync(Guid companyId, Guid userId)
        => await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
}
