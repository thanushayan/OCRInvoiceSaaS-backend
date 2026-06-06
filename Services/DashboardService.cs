using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _db;

    public DashboardService(ApplicationDbContext db) { _db = db; }

    public async Task<ServiceResult<DashboardResponse>> GetDashboardAsync(Guid companyId, Guid userId)
    {
        bool hasAccess = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!hasAccess) return ServiceResult<DashboardResponse>.Fail("Access denied.", 403);

        var invoices = await _db.Invoices
            .Include(i => i.Vendor)
            .Where(i => i.CompanyId == companyId)
            .ToListAsync();

        var monthlySummary = invoices
            .Where(i => i.CreatedAt >= DateTime.UtcNow.AddMonths(-12))
            .GroupBy(i => new { i.CreatedAt.Year, i.CreatedAt.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new MonthlyInvoiceSummary
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                MonthName = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMMM yyyy"),
                InvoiceCount = g.Count(),
                TotalAmount = g.Sum(i => i.TotalAmount ?? 0)
            })
            .ToList();

        var recentInvoices = invoices
            .OrderByDescending(i => i.CreatedAt)
            .Take(5)
            .Select(i => new InvoiceListResponse
            {
                Id = i.Id,
                FileName = i.FileName,
                InvoiceNumber = i.InvoiceNumber,
                TotalAmount = i.TotalAmount,
                Currency = i.Currency,
                Status = i.Status.ToString(),
                VendorName = i.Vendor?.Name ?? i.ExtractedVendorName,
                CreatedAt = i.CreatedAt
            })
            .ToList();

        return ServiceResult<DashboardResponse>.Success(new DashboardResponse
        {
            TotalInvoices = invoices.Count,
            ProcessedInvoices = invoices.Count(i => i.Status >= InvoiceStatus.Processed),
            PendingInvoices = invoices.Count(i => i.Status == InvoiceStatus.Uploaded || i.Status == InvoiceStatus.Processing),
            ApprovedInvoices = invoices.Count(i => i.Status == InvoiceStatus.Approved),
            TotalAmount = invoices.Sum(i => i.TotalAmount ?? 0),
            MonthlySummary = monthlySummary,
            RecentInvoices = recentInvoices
        });
    }
}
