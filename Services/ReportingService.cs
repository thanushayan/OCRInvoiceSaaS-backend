using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

/// <summary>
/// Spend analytics, VAT summaries and CSV/Excel exports.
/// Amounts are reported in the company base currency where an exchange
/// rate was attached (BaseCurrencyAmount), falling back to the raw total.
/// </summary>
public class ReportingService : IReportingService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    private string BaseCurrency => _config["Currency:BaseCurrency"] ?? "GBP";

    public ReportingService(ApplicationDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<ServiceResult<SpendAnalyticsResponse>> GetSpendAnalyticsAsync(
        Guid companyId, ReportFilterRequest filter, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<SpendAnalyticsResponse>.Fail("Access denied.", 403);

        var invoices = await FilteredInvoices(companyId, filter)
            .Include(i => i.Vendor)
            .Include(i => i.ExpenseCategory)
            .ToListAsync();

        decimal Spend(Invoice i) => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0m;

        var totalSpend = invoices.Sum(Spend);

        var byVendor = invoices
            .GroupBy(i => new { i.VendorId, Name = i.Vendor != null ? i.Vendor.Name : (i.ExtractedVendorName ?? "Unknown") })
            .Select(g => new SpendByVendor
            {
                VendorId = g.Key.VendorId,
                VendorName = g.Key.Name,
                InvoiceCount = g.Count(),
                TotalAmount = Math.Round(g.Sum(Spend), 2),
                Percentage = totalSpend > 0 ? Math.Round(g.Sum(Spend) / totalSpend * 100, 1) : 0
            })
            .OrderByDescending(v => v.TotalAmount)
            .ToList();

        var byCategory = invoices
            .GroupBy(i => new { i.ExpenseCategoryId, Name = i.ExpenseCategory != null ? i.ExpenseCategory.Name : "Uncategorised" })
            .Select(g => new SpendByCategory
            {
                CategoryId = g.Key.ExpenseCategoryId,
                CategoryName = g.Key.Name,
                InvoiceCount = g.Count(),
                TotalAmount = Math.Round(g.Sum(Spend), 2),
                Percentage = totalSpend > 0 ? Math.Round(g.Sum(Spend) / totalSpend * 100, 1) : 0
            })
            .OrderByDescending(c => c.TotalAmount)
            .ToList();

        var byMonth = invoices
            .Where(i => (i.InvoiceDate ?? i.CreatedAt) != default)
            .GroupBy(i => new { (i.InvoiceDate ?? i.CreatedAt).Year, (i.InvoiceDate ?? i.CreatedAt).Month })
            .Select(g => new SpendByMonth
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                MonthLabel = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                InvoiceCount = g.Count(),
                TotalAmount = Math.Round(g.Sum(Spend), 2),
                TaxAmount = Math.Round(g.Sum(i => i.TaxAmount ?? 0m), 2)
            })
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();

        var byCurrency = invoices
            .GroupBy(i => i.Currency ?? "GBP")
            .Select(g => new SpendByCurrency
            {
                Currency = g.Key,
                InvoiceCount = g.Count(),
                TotalOriginalAmount = Math.Round(g.Sum(i => i.TotalAmount ?? 0m), 2),
                TotalBaseCurrencyAmount = Math.Round(g.Sum(Spend), 2)
            })
            .OrderByDescending(c => c.TotalBaseCurrencyAmount)
            .ToList();

        return ServiceResult<SpendAnalyticsResponse>.Success(new SpendAnalyticsResponse
        {
            TotalSpend = Math.Round(totalSpend, 2),
            TotalTax = Math.Round(invoices.Sum(i => i.TaxAmount ?? 0m), 2),
            TotalInvoices = invoices.Count,
            BaseCurrency = BaseCurrency,
            ByVendor = byVendor,
            ByCategory = byCategory,
            ByMonth = byMonth,
            ByCurrency = byCurrency
        });
    }

    public async Task<ServiceResult<VatSummaryResponse>> GetVatSummaryAsync(
        Guid companyId, DateTime periodStart, DateTime periodEnd, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<VatSummaryResponse>.Fail("Access denied.", 403);
        if (periodEnd < periodStart)
            return ServiceResult<VatSummaryResponse>.Fail("periodEnd must be after periodStart.", 400);

        var invoices = await _db.Invoices
            .Include(i => i.InvoiceItems)
            .Where(i => i.CompanyId == companyId &&
                        (i.InvoiceDate ?? i.CreatedAt) >= periodStart &&
                        (i.InvoiceDate ?? i.CreatedAt) <= periodEnd)
            .ToListAsync();

        var totalVat = invoices.Sum(i => i.TaxAmount ?? 0m);
        var totalGross = invoices.Sum(i => i.TotalAmount ?? 0m);
        var totalNet = invoices.Sum(i => i.SubTotal ?? ((i.TotalAmount ?? 0m) - (i.TaxAmount ?? 0m)));

        var byRate = invoices
            .SelectMany(i => i.InvoiceItems)
            .Where(it => it.TaxRate.HasValue)
            .GroupBy(it => it.TaxRate!.Value)
            .Select(g => new VatByRate
            {
                TaxRate = g.Key,
                NetAmount = Math.Round(g.Sum(it => it.LineTotal), 2),
                VatAmount = Math.Round(g.Sum(it => it.TaxAmount ?? it.LineTotal * g.Key / 100m), 2),
                InvoiceCount = g.Select(it => it.InvoiceId).Distinct().Count()
            })
            .OrderBy(r => r.TaxRate)
            .ToList();

        return ServiceResult<VatSummaryResponse>.Success(new VatSummaryResponse
        {
            Period = $"{periodStart:yyyy-MM-dd} — {periodEnd:yyyy-MM-dd}",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            BaseCurrency = BaseCurrency,
            TotalNetAmount = Math.Round(totalNet, 2),
            TotalVatAmount = Math.Round(totalVat, 2),
            TotalGrossAmount = Math.Round(totalGross, 2),
            InvoiceCount = invoices.Count,
            ByRate = byRate
        });
    }

    public async Task<ServiceResult<byte[]>> ExportInvoicesCsvAsync(
        Guid companyId, ReportFilterRequest filter, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<byte[]>.Fail("Access denied.", 403);

        var rows = await ExportRowsAsync(companyId, filter);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ExportHeaders));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(CsvEscape)));

        return ServiceResult<byte[]>.Success(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public async Task<ServiceResult<byte[]>> ExportInvoicesExcelAsync(
        Guid companyId, ReportFilterRequest filter, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<byte[]>.Fail("Access denied.", 403);

        var rows = await ExportRowsAsync(companyId, filter);
        return ServiceResult<byte[]>.Success(BuildXlsx(ExportHeaders, rows));
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static readonly string[] ExportHeaders =
    [
        "InvoiceNumber", "FileName", "Status", "VendorName", "Category",
        "InvoiceDate", "DueDate", "Currency", "SubTotal", "TaxAmount",
        "TotalAmount", "BaseCurrency", "BaseCurrencyAmount", "UploadedAt"
    ];

    private async Task<List<string[]>> ExportRowsAsync(Guid companyId, ReportFilterRequest filter)
    {
        var invoices = await FilteredInvoices(companyId, filter)
            .Include(i => i.Vendor)
            .Include(i => i.ExpenseCategory)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return invoices.Select(i => new[]
        {
            i.InvoiceNumber ?? "",
            i.FileName,
            i.Status.ToString(),
            i.Vendor?.Name ?? i.ExtractedVendorName ?? "",
            i.ExpenseCategory?.Name ?? "",
            i.InvoiceDate?.ToString("yyyy-MM-dd") ?? "",
            i.DueDate?.ToString("yyyy-MM-dd") ?? "",
            i.Currency ?? "GBP",
            i.SubTotal?.ToString("0.00") ?? "",
            i.TaxAmount?.ToString("0.00") ?? "",
            i.TotalAmount?.ToString("0.00") ?? "",
            i.BaseCurrency ?? "",
            i.BaseCurrencyAmount?.ToString("0.00") ?? "",
            i.CreatedAt.ToString("yyyy-MM-dd HH:mm")
        }).ToList();
    }

    private IQueryable<Invoice> FilteredInvoices(Guid companyId, ReportFilterRequest filter)
    {
        var q = _db.Invoices.Where(i => i.CompanyId == companyId);

        if (filter.From.HasValue)
            q = q.Where(i => (i.InvoiceDate ?? i.CreatedAt) >= filter.From.Value);
        if (filter.To.HasValue)
            q = q.Where(i => (i.InvoiceDate ?? i.CreatedAt) <= filter.To.Value);
        if (!string.IsNullOrWhiteSpace(filter.Currency))
            q = q.Where(i => i.Currency == filter.Currency.ToUpper());
        if (filter.VendorId.HasValue)
            q = q.Where(i => i.VendorId == filter.VendorId.Value);
        if (filter.CategoryId.HasValue)
            q = q.Where(i => i.ExpenseCategoryId == filter.CategoryId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status) &&
            Enum.TryParse<InvoiceStatus>(filter.Status, true, out var status))
            q = q.Where(i => i.Status == status);

        return q;
    }

    private static string CsvEscape(string value)
        => value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    /// <summary>
    /// Builds a minimal but valid .xlsx (Office Open XML) workbook with one
    /// sheet, using inline strings — no third-party Excel dependency needed.
    /// </summary>
    private static byte[] BuildXlsx(string[] headers, List<string[]> rows)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);

            AddEntry(zip, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);

            AddEntry(zip, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Invoices" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);

            AddEntry(zip, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            var sheet = new StringBuilder();
            sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
            sheet.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
            AppendXlsxRow(sheet, headers);
            foreach (var row in rows)
                AppendXlsxRow(sheet, row);
            sheet.Append("</sheetData></worksheet>");

            AddEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return stream.ToArray();
    }

    private static void AppendXlsxRow(StringBuilder sheet, string[] cells)
    {
        sheet.Append("<row>");
        foreach (var cell in cells)
        {
            var escaped = System.Security.SecurityElement.Escape(cell) ?? "";
            sheet.Append("<c t=\"inlineStr\"><is><t>").Append(escaped).Append("</t></is></c>");
        }
        sheet.Append("</row>");
    }

    private static void AddEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private async Task<bool> IsMemberAsync(Guid companyId, Guid userId)
        => await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
}
