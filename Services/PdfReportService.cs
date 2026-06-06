using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OcrInvoiceSaaS.Services;

/// <summary>
/// Generates PDF reports using QuestPDF (https://www.questpdf.com — MIT licensed).
/// Add to project: dotnet add package QuestPDF
/// </summary>
public class PdfReportService
{
    private readonly ApplicationDbContext _db;
    private readonly IReportingService _reportingService;

    private static readonly string BrandBlue    = "#1A56DB";
    private static readonly string BrandGray    = "#6B7280";
    private static readonly string LightBg      = "#F9FAFB";
    private static readonly string BorderColor  = "#E5E7EB";
    private static readonly string DangerRed    = "#DC2626";

    public PdfReportService(ApplicationDbContext db, IReportingService reportingService)
    {
        _db               = db;
        _reportingService = reportingService;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<ServiceResult<byte[]>> GenerateMonthlySpendReportAsync(
        Guid companyId, MonthlySpendReportRequest request, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<byte[]>.Fail("Access denied.", 403);

        var company = await _db.Companies.FindAsync(companyId);
        if (company == null) return ServiceResult<byte[]>.Fail("Company not found.", 404);

        var periodStart = new DateTime(request.Year, request.Month, 1);
        var periodEnd   = periodStart.AddMonths(1).AddDays(-1);

        var analyticsResult = await _reportingService.GetSpendAnalyticsAsync(companyId,
            new ReportFilterRequest { From = periodStart, To = periodEnd, Currency = request.Currency, Status = "Approved" }, userId);
        if (!analyticsResult.IsSuccess) return ServiceResult<byte[]>.Fail(analyticsResult.Error!, 500);

        var analytics = analyticsResult.Data!;
        var invoices = await _db.Invoices
            .Include(i => i.Vendor).Include(i => i.ExpenseCategory)
            .Where(i => i.CompanyId == companyId && i.InvoiceDate >= periodStart && i.InvoiceDate <= periodEnd && i.Status == InvoiceStatus.Approved)
            .OrderBy(i => i.InvoiceDate).ToListAsync();

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4); page.Margin(40);
                page.DefaultTextStyle(x => x.FontFamily("Helvetica").FontSize(9));
                page.Header().Element(c => BuildHeader(c, company.Name, $"Monthly Spend Report — {periodStart:MMMM yyyy}", userId));
                page.Content().Column(col =>
                {
                    col.Spacing(16);
                    col.Item().Element(c => BuildSummaryCards(c, analytics));
                    if (request.IncludeVendorBreakdown && analytics.ByVendor.Count > 0)
                        col.Item().Element(c => BuildSection(c, "Spend by Vendor", t => BuildVendorTable(t, analytics.ByVendor)));
                    if (request.IncludeCategoryBreakdown && analytics.ByCategory.Count > 0)
                        col.Item().Element(c => BuildSection(c, "Spend by Category", t => BuildCategoryTable(t, analytics.ByCategory)));
                    if (request.IncludeInvoiceList && invoices.Count > 0)
                        col.Item().Element(c => BuildSection(c, "Invoice Detail", t => BuildInvoiceTable(t, invoices)));
                });
                page.Footer().Element(c => BuildFooter(c, company.Name));
            });
        });

        using var ms = new MemoryStream();
        pdf.GeneratePdf(ms);
        return ServiceResult<byte[]>.Success(ms.ToArray());
    }

    public async Task<ServiceResult<byte[]>> GenerateVatReportAsync(
        Guid companyId, VatReportPdfRequest request, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<byte[]>.Fail("Access denied.", 403);

        var company = await _db.Companies.FindAsync(companyId);
        if (company == null) return ServiceResult<byte[]>.Fail("Company not found.", 404);

        var vatResult = await _reportingService.GetVatSummaryAsync(companyId, request.PeriodStart, request.PeriodEnd, userId);
        if (!vatResult.IsSuccess) return ServiceResult<byte[]>.Fail(vatResult.Error!, 500);
        var vat = vatResult.Data!;

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4); page.Margin(40);
                page.DefaultTextStyle(x => x.FontFamily("Helvetica").FontSize(9));
                page.Header().Element(c => BuildHeader(c, company.Name, $"VAT Report — {vat.Period}", userId));
                page.Content().Column(col =>
                {
                    col.Spacing(16);
                    col.Item().Element(c => BuildSection(c, "VAT Summary", t =>
                    {
                        t.Table(table =>
                        {
                            table.ColumnsDefinition(cols => { cols.RelativeColumn(3); cols.RelativeColumn(2); });
                            void Row(string label, decimal value, bool highlight = false)
                            {
                                table.Cell().Padding(6).Text(label).FontSize(9).FontColor(highlight ? BrandBlue : Colors.Black);
                                table.Cell().Padding(6).AlignRight().Text($"{vat.BaseCurrency} {value:N2}").FontSize(9).Bold().FontColor(highlight ? BrandBlue : Colors.Black);
                            }
                            Row("Total Net Amount (ex-VAT)", vat.TotalNetAmount);
                            Row("Total VAT Amount", vat.TotalVatAmount);
                            Row("Total Gross Amount", vat.TotalGrossAmount, true);
                        });
                    }));
                    col.Item().Background(LightBg).Border(1).BorderColor(BorderColor).Padding(12).Column(note =>
                    {
                        note.Item().Text("HMRC MTD Submission Notes").Bold().FontSize(10);
                        note.Item().PaddingTop(4).Text("This report is for reference only. Submit via the MTD VAT Return endpoint for HMRC compliance.");
                    });
                });
                page.Footer().Element(c => BuildFooter(c, company.Name));
            });
        });

        using var ms = new MemoryStream();
        pdf.GeneratePdf(ms);
        return ServiceResult<byte[]>.Success(ms.ToArray());
    }

    private static void BuildHeader(IContainer container, string companyName, string title, Guid userId)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(companyName).Bold().FontSize(14).FontColor(BrandBlue);
                col.Item().Text(title).FontSize(11);
                col.Item().PaddingTop(2).Text($"Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontSize(8).FontColor(BrandGray);
            });
            row.ConstantItem(80).AlignRight().AlignMiddle().Text("OCR Invoice SaaS").FontSize(8).FontColor(BrandGray);
        });
        container.PaddingTop(8).LineHorizontal(1).LineColor(BrandBlue);
    }

    private static void BuildFooter(IContainer container, string companyName)
    {
        container.LineHorizontal(1).LineColor(BorderColor);
        container.PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(companyName).FontSize(8).FontColor(BrandGray);
            row.RelativeItem().AlignCenter().Text(text => { text.Span("Page ").FontSize(8); text.CurrentPageNumber().FontSize(8); text.Span(" of ").FontSize(8); text.TotalPages().FontSize(8); });
            row.RelativeItem().AlignRight().Text("Confidential").FontSize(8).FontColor(BrandGray);
        });
    }

    private static void BuildSection(IContainer container, string title, Action<IContainer> content)
    {
        container.Column(col =>
        {
            col.Item().Text(title).Bold().FontSize(11).FontColor(BrandBlue);
            col.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(BorderColor);
            col.Item().PaddingTop(6).Element(content);
        });
    }

    private static void BuildSummaryCards(IContainer container, SpendAnalyticsResponse analytics)
    {
        container.Row(row =>
        {
            void Card(string label, string value)
            {
                row.RelativeItem().Border(1).BorderColor(BorderColor).Background(LightBg).Padding(10).Column(col =>
                { col.Item().Text(label).FontSize(8).FontColor(BrandGray); col.Item().PaddingTop(2).Text(value).Bold().FontSize(14).FontColor(BrandBlue); });
            }
            Card("Total Spend", $"{analytics.BaseCurrency} {analytics.TotalSpend:N2}");
            row.ConstantItem(8);
            Card("Total Tax (VAT)", $"{analytics.BaseCurrency} {analytics.TotalTax:N2}");
            row.ConstantItem(8);
            Card("Total Invoices", analytics.TotalInvoices.ToString("N0"));
        });
    }

    private static void BuildVendorTable(IContainer container, List<SpendByVendor> vendors)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols => { cols.RelativeColumn(3); cols.RelativeColumn(2); cols.RelativeColumn(2); cols.ConstantColumn(50); });
            void H(string t) => table.Cell().Background(LightBg).Padding(6).Text(t).Bold().FontSize(8).FontColor(BrandGray);
            H("Vendor"); H("Total Spend"); H("Invoices"); H("Share");
            foreach (var v in vendors.Take(20))
            {
                table.Cell().Padding(6).Text(v.VendorName).FontSize(9);
                table.Cell().Padding(6).AlignRight().Text($"{v.TotalAmount:N2}").FontSize(9);
                table.Cell().Padding(6).AlignRight().Text(v.InvoiceCount.ToString()).FontSize(9);
                table.Cell().Padding(6).AlignRight().Text($"{v.Percentage:0.0}%").FontSize(9).FontColor(v.Percentage > 20 ? DangerRed : Colors.Black);
            }
        });
    }

    private static void BuildCategoryTable(IContainer container, List<SpendByCategory> categories)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols => { cols.RelativeColumn(3); cols.RelativeColumn(2); cols.RelativeColumn(2); cols.ConstantColumn(50); });
            void H(string t) => table.Cell().Background(LightBg).Padding(6).Text(t).Bold().FontSize(8).FontColor(BrandGray);
            H("Category"); H("Total Spend"); H("Invoices"); H("Share");
            foreach (var c in categories)
            {
                table.Cell().Padding(6).Text(c.CategoryName).FontSize(9);
                table.Cell().Padding(6).AlignRight().Text($"{c.TotalAmount:N2}").FontSize(9);
                table.Cell().Padding(6).AlignRight().Text(c.InvoiceCount.ToString()).FontSize(9);
                table.Cell().Padding(6).AlignRight().Text($"{c.Percentage:0.0}%").FontSize(9);
            }
        });
    }

    private static void BuildInvoiceTable(IContainer container, List<Invoice> invoices)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols => { cols.RelativeColumn(2); cols.RelativeColumn(3); cols.ConstantColumn(80); cols.RelativeColumn(2); cols.RelativeColumn(2); });
            void H(string t) => table.Cell().Background(LightBg).Padding(6).Text(t).Bold().FontSize(8).FontColor(BrandGray);
            H("Invoice #"); H("Vendor"); H("Date"); H("Net"); H("Total");
            foreach (var inv in invoices)
            {
                table.Cell().Padding(5).Text(inv.InvoiceNumber ?? "—").FontSize(8);
                table.Cell().Padding(5).Text(inv.Vendor?.Name ?? inv.ExtractedVendorName ?? "—").FontSize(8);
                table.Cell().Padding(5).Text(inv.InvoiceDate?.ToString("dd/MM/yy") ?? "—").FontSize(8);
                table.Cell().Padding(5).AlignRight().Text($"{inv.SubTotal:N2}").FontSize(8);
                table.Cell().Padding(5).AlignRight().Text($"{inv.TotalAmount:N2}").FontSize(8).Bold();
            }
        });
    }
}
