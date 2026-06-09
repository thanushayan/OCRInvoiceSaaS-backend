using System.Text;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class ReportingService : IReportingService
{
    private readonly ApplicationDbContext _db;

    public ReportingService(ApplicationDbContext db) => _db = db;

    // ── Spend Analytics ───────────────────────────────────────────────────────

    public async Task<ServiceResult<SpendAnalyticsResponse>> GetSpendAnalyticsAsync(
        Guid companyId, ReportFilterRequest filter, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<SpendAnalyticsResponse>.Fail("Access denied.", 403);

        var query = BuildBaseQuery(companyId, filter);

        var invoices = await query
            .Include(i => i.Vendor)
            .Include(i => i.ExpenseCategory)
            .ToListAsync();

        if (invoices.Count == 0)
            return ServiceResult<SpendAnalyticsResponse>.Success(new SpendAnalyticsResponse());

        var totalSpend = invoices.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0);

        // By Vendor
        var byVendor = invoices
            .GroupBy(i => new
            {
                VendorId = i.VendorId,
                Name = i.Vendor?.Name ?? i.ExtractedVendorName ?? "Unknown Vendor"
            })
            .Select(g => new SpendByVendor
            {
                VendorId     = g.Key.VendorId,
                VendorName   = g.Key.Name,
                InvoiceCount = g.Count(),
                TotalAmount  = g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                Percentage   = totalSpend > 0
                    ? Math.Round(g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0) / totalSpend * 100, 1)
                    : 0
            })
            .OrderByDescending(v => v.TotalAmount)
            .ToList();

        // By Category
        var byCategory = invoices
            .GroupBy(i => new
            {
                CategoryId = i.ExpenseCategoryId,
                Name = i.ExpenseCategory?.Name ?? "Uncategorised"
            })
            .Select(g => new SpendByCategory
            {
                CategoryId   = g.Key.CategoryId,
                CategoryName = g.Key.Name,
                InvoiceCount = g.Count(),
                TotalAmount  = g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                Percentage   = totalSpend > 0
                    ? Math.Round(g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0) / totalSpend * 100, 1)
                    : 0
            })
            .OrderByDescending(c => c.TotalAmount)
            .ToList();

        // By Month
        var byMonth = invoices
            .Where(i => i.InvoiceDate.HasValue)
            .GroupBy(i => new { i.InvoiceDate!.Value.Year, i.InvoiceDate.Value.Month })
            .Select(g => new SpendByMonth
            {
                Year         = g.Key.Year,
                Month        = g.Key.Month,
                MonthLabel   = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                InvoiceCount = g.Count(),
                TotalAmount  = g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0),
                TaxAmount    = g.Sum(i => i.TaxAmount ?? 0)
            })
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();

        // By Currency
        var byCurrency = invoices
            .GroupBy(i => i.Currency ?? "GBP")
            .Select(g => new SpendByCurrency
            {
                Currency                  = g.Key,
                InvoiceCount              = g.Count(),
                TotalOriginalAmount       = g.Sum(i => i.TotalAmount ?? 0),
                TotalBaseCurrencyAmount   = g.Sum(i => i.BaseCurrencyAmount ?? i.TotalAmount ?? 0)
            })
            .OrderByDescending(c => c.TotalBaseCurrencyAmount)
            .ToList();

        return ServiceResult<SpendAnalyticsResponse>.Success(new SpendAnalyticsResponse
        {
            TotalSpend    = totalSpend,
            TotalTax      = invoices.Sum(i => i.TaxAmount ?? 0),
            TotalInvoices = invoices.Count,
            BaseCurrency  = invoices.FirstOrDefault()?.BaseCurrency ?? "GBP",
            ByVendor      = byVendor,
            ByCategory    = byCategory,
            ByMonth       = byMonth,
            ByCurrency    = byCurrency
        });
    }

    // ── VAT Summary ───────────────────────────────────────────────────────────

    public async Task<ServiceResult<VatSummaryResponse>> GetVatSummaryAsync(
        Guid companyId, DateTime periodStart, DateTime periodEnd, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<VatSummaryResponse>.Fail("Access denied.", 403);

        var invoices = await _db.Invoices
            .Include(i => i.InvoiceItems)
            .Where(i =>
                i.CompanyId == companyId &&
                i.Status == InvoiceStatus.Approved &&
                i.InvoiceDate >= periodStart &&
                i.InvoiceDate <= periodEnd)
            .ToListAsync();

        var byRate = invoices
            .SelectMany(i => i.InvoiceItems.Select(item => new
            {
                TaxRate    = item.TaxRate ?? 20m,
                NetAmount  = item.LineTotal,
                VatAmount  = item.TaxRate.HasValue ? Math.Round(item.LineTotal * item.TaxRate.Value / 100, 2) : 0
            }))
            .GroupBy(x => x.TaxRate)
            .Select(g => new VatByRate
            {
                TaxRate      = g.Key,
                NetAmount    = g.Sum(x => x.NetAmount),
                VatAmount    = g.Sum(x => x.VatAmount),
                InvoiceCount = invoices.Count
            })
            .OrderByDescending(r => r.TaxRate)
            .ToList();

        return ServiceResult<VatSummaryResponse>.Success(new VatSummaryResponse
        {
            Period           = $"{periodStart:dd MMM yyyy} – {periodEnd:dd MMM yyyy}",
            PeriodStart      = periodStart,
            PeriodEnd        = periodEnd,
            BaseCurrency     = "GBP",
            TotalNetAmount   = invoices.Sum(i => i.SubTotal ?? 0),
            TotalVatAmount   = invoices.Sum(i => i.TaxAmount ?? 0),
            TotalGrossAmount = invoices.Sum(i => i.TotalAmount ?? 0),
            InvoiceCount     = invoices.Count,
            ByRate           = byRate
        });
    }

    // ── CSV Export ────────────────────────────────────────────────────────────

    public async Task<ServiceResult<byte[]>> ExportInvoicesCsvAsync(
        Guid companyId, ReportFilterRequest filter, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<byte[]>.Fail("Access denied.", 403);

        var invoices = await BuildBaseQuery(companyId, filter)
            .Include(i => i.Vendor)
            .Include(i => i.ExpenseCategory)
            .Include(i => i.UploadedByUser)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Invoice Number,Vendor,Invoice Date,Due Date,Currency,Net Amount,Tax Amount,Total Amount,Base Currency Amount,Category,Status,Uploaded By,Uploaded At");

        foreach (var inv in invoices)
        {
            sb.AppendLine(string.Join(",",
                CsvEscape(inv.InvoiceNumber ?? ""),
                CsvEscape(inv.Vendor?.Name ?? inv.ExtractedVendorName ?? ""),
                inv.InvoiceDate?.ToString("yyyy-MM-dd") ?? "",
                inv.DueDate?.ToString("yyyy-MM-dd") ?? "",
                inv.Currency ?? "GBP",
                inv.SubTotal?.ToString("F2") ?? "",
                inv.TaxAmount?.ToString("F2") ?? "",
                inv.TotalAmount?.ToString("F2") ?? "",
                inv.BaseCurrencyAmount?.ToString("F2") ?? "",
                CsvEscape(inv.ExpenseCategory?.Name ?? ""),
                inv.Status.ToString(),
                CsvEscape(inv.UploadedByUser?.FullName ?? ""),
                inv.CreatedAt.ToString("yyyy-MM-dd HH:mm")
            ));
        }

        return ServiceResult<byte[]>.Success(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    // ── Excel Export (OOXML without third-party library) ─────────────────────

    public async Task<ServiceResult<byte[]>> ExportInvoicesExcelAsync(
        Guid companyId, ReportFilterRequest filter, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<byte[]>.Fail("Access denied.", 403);

        var invoices = await BuildBaseQuery(companyId, filter)
            .Include(i => i.Vendor)
            .Include(i => i.ExpenseCategory)
            .Include(i => i.UploadedByUser)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        // Build a proper OOXML .xlsx using SpreadsheetML (no third-party needed)
        var xlsx = BuildXlsx(invoices, companyId);
        return ServiceResult<byte[]>.Success(xlsx);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IQueryable<Invoice> BuildBaseQuery(Guid companyId, ReportFilterRequest filter)
    {
        var query = _db.Invoices.Where(i => i.CompanyId == companyId);

        if (filter.From.HasValue)
            query = query.Where(i => i.InvoiceDate >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(i => i.InvoiceDate <= filter.To.Value);
        if (!string.IsNullOrWhiteSpace(filter.Currency))
            query = query.Where(i => i.Currency == filter.Currency.ToUpper());
        if (filter.VendorId.HasValue)
            query = query.Where(i => i.VendorId == filter.VendorId.Value);
        if (filter.CategoryId.HasValue)
            query = query.Where(i => i.ExpenseCategoryId == filter.CategoryId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status) &&
            Enum.TryParse<InvoiceStatus>(filter.Status, true, out var status))
            query = query.Where(i => i.Status == status);

        return query;
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private async Task<bool> IsMemberAsync(Guid companyId, Guid userId)
        => await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);

    // Minimal OOXML SpreadsheetML builder — no NuGet dependency required
    private static byte[] BuildXlsx(List<Invoice> invoices, Guid companyId)
    {
        var headers = new[]
        {
            "Invoice Number", "Vendor", "Invoice Date", "Due Date", "Currency",
            "Net Amount", "Tax Amount", "Total Amount", "Base Amount", "Category", "Status"
        };

        var rows = new StringBuilder();
        rows.AppendLine(BuildXlsxRow(1, headers.Select(XmlEscape).ToArray(), isHeader: true));

        int rowNum = 2;
        foreach (var inv in invoices)
        {
            rows.AppendLine(BuildXlsxRow(rowNum++, new[]
            {
                XmlEscape(inv.InvoiceNumber ?? ""),
                XmlEscape(inv.ExtractedVendorName ?? ""),
                inv.InvoiceDate?.ToString("yyyy-MM-dd") ?? "",
                inv.DueDate?.ToString("yyyy-MM-dd") ?? "",
                inv.Currency ?? "GBP",
                inv.SubTotal?.ToString("F2") ?? "",
                inv.TaxAmount?.ToString("F2") ?? "",
                inv.TotalAmount?.ToString("F2") ?? "",
                inv.BaseCurrencyAmount?.ToString("F2") ?? "",
                XmlEscape(inv.ExpenseCategory?.Name ?? ""),
                inv.Status.ToString()
            }));
        }

        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <sheets><sheet name=""Invoices"" sheetId=""1"" r:id=""rId1""/></sheets>
</workbook>";

        var sheetXml = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <sheetData>{rows}</sheetData>
</worksheet>";

        // Package as a proper zip-based .xlsx (OOXML)
        using var ms = new System.IO.MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            void AddEntry(string path, string content)
            {
                var entry = archive.CreateEntry(path);
                using var sw = new System.IO.StreamWriter(entry.Open());
                sw.Write(content);
            }

            AddEntry("[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
</Types>");

            AddEntry("_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

            AddEntry("xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
</Relationships>");

            AddEntry("xl/workbook.xml", xml);
            AddEntry("xl/worksheets/sheet1.xml", sheetXml);
        }

        return ms.ToArray();
    }

    private static string BuildXlsxRow(int rowNum, string[] cells, bool isHeader = false)
    {
        var sb = new StringBuilder();
        sb.Append($"<row r=\"{rowNum}\">");
        for (int i = 0; i < cells.Length; i++)
        {
            var col = (char)('A' + i);
            sb.Append($"<c r=\"{col}{rowNum}\" t=\"inlineStr\"><is><t>{cells[i]}</t></is></c>");
        }
        sb.Append("</row>");
        return sb.ToString();
    }

    private static string XmlEscape(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
