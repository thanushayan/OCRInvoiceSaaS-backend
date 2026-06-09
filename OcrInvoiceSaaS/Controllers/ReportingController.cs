using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/reports")]
public class ReportingController : ControllerBase
{
    private readonly IReportingService _reporting;
    private readonly PdfReportService _pdfService;
    private readonly MtdVatService _mtdService;
    private readonly SpendAnalyticsService _analytics;
    private readonly CurrentUserProvider _currentUser;

    public ReportingController(
        IReportingService reporting,
        PdfReportService pdfService,
        MtdVatService mtdService,
        SpendAnalyticsService analytics,
        CurrentUserProvider currentUser)
    {
        _reporting   = reporting;
        _pdfService  = pdfService;
        _mtdService  = mtdService;
        _analytics   = analytics;
        _currentUser = currentUser;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Spend Analytics (JSON)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Full spend analytics — totals by vendor, category, month, and currency.
    /// Supports filters: from, to, currency, vendorId, categoryId, status.
    /// </summary>
    [HttpGet("spend")]
    public async Task<IActionResult> GetSpend(Guid companyId, [FromQuery] ReportFilterRequest filter)
    {
        var result = await _reporting.GetSpendAnalyticsAsync(companyId, filter, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Spend trend over time — daily, weekly, monthly, or quarterly data points.
    /// Set compareWithPreviousPeriod=true to include previous period data for YoY / MoM comparison.
    /// </summary>
    [HttpGet("spend/trend")]
    public async Task<IActionResult> GetTrend(Guid companyId, [FromQuery] SpendTrendRequest request)
    {
        var result = await _analytics.GetSpendTrendAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Top N vendors by spend. Use ?top=10&from=2026-01-01&to=2026-12-31.
    /// </summary>
    [HttpGet("spend/top-vendors")]
    public async Task<IActionResult> GetTopVendors(Guid companyId, [FromQuery] TopVendorsRequest request)
    {
        var result = await _analytics.GetTopVendorsAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Vendor drill-down — all invoices, monthly trend, and stats for a single vendor.
    /// </summary>
    [HttpGet("spend/vendors/{vendorId:guid}")]
    public async Task<IActionResult> GetVendorDrilldown(
        Guid companyId, Guid vendorId, [FromQuery] ReportFilterRequest filter)
    {
        var result = await _analytics.GetVendorDrilldownAsync(companyId, vendorId, filter, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Category drill-down — total spend, top vendors, and monthly trend for a single category.
    /// </summary>
    [HttpGet("spend/categories/{categoryId:guid}")]
    public async Task<IActionResult> GetCategoryDrilldown(
        Guid companyId, Guid categoryId, [FromQuery] ReportFilterRequest filter)
    {
        var result = await _analytics.GetCategoryDrilldownAsync(companyId, categoryId, filter, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VAT (JSON)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// VAT summary for a date range — net, VAT, and gross totals grouped by tax rate.
    /// Covers approved invoices only.
    /// </summary>
    [HttpGet("vat")]
    public async Task<IActionResult> GetVat(Guid companyId,
        [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var result = await _reporting.GetVatSummaryAsync(companyId, from, to, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MTD (Making Tax Digital) VAT Return
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculate a UK MTD VAT return (Boxes 1–9) for the given period.
    ///
    /// This system tracks PURCHASE invoices (input tax), so:
    ///   Box 4 (VAT reclaimed) and Box 7 (total purchases) are auto-calculated.
    ///   Box 1 (output VAT on sales) and Box 6 (net sales) must be provided
    ///   manually from the company's sales ledger.
    ///
    /// Returns validation warnings for any data quality issues before submission.
    /// HMRC MTD API: https://developer.service.hmrc.gov.uk/api-documentation/docs/api/service/vat-api
    /// </summary>
    [HttpPost("mtd/vat-return")]
    public async Task<IActionResult> CalculateMtdVatReturn(
        Guid companyId, [FromBody] MtdVatReturnRequest request)
    {
        var result = await _mtdService.CalculateAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Validate an MTD VAT return without generating it — runs data quality checks only.
    /// Useful for pre-flight checks before period end.
    /// </summary>
    [HttpGet("mtd/vat-return/validate")]
    public async Task<IActionResult> ValidateMtdData(
        Guid companyId, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        // Lightweight validation — use zero manual figures to see warnings
        var request = new MtdVatReturnRequest
        {
            PeriodStart               = from,
            PeriodEnd                 = to,
            ManualBox1OutputVat       = 0,
            ManualBox6SalesExcludingVat = 0
        };

        var result = await _mtdService.CalculateAsync(companyId, request, _currentUser.GetUserId());
        if (!result.IsSuccess) return StatusCode(result.StatusCode, new { error = result.Error });

        return Ok(new
        {
            isValid            = result.Data!.IsValid,
            warnings           = result.Data.ValidationWarnings,
            invoiceCount       = result.Data.InvoiceCount,
            calculatedBox4     = result.Data.Box4VatReclaimedOnPurchases,
            calculatedBox7     = result.Data.Box7TotalPurchasesExcludingVat,
            period             = result.Data.PeriodKey,
            statusMessage      = result.Data.StatusMessage
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Exports (CSV / Excel)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export invoices as a UTF-8 CSV file.
    /// Supports all standard filter parameters.
    /// </summary>
    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv(Guid companyId, [FromQuery] ReportFilterRequest filter)
    {
        var result = await _reporting.ExportInvoicesCsvAsync(companyId, filter, _currentUser.GetUserId());
        if (!result.IsSuccess) return StatusCode(result.StatusCode, new { error = result.Error });

        return File(result.Data!, "text/csv",
            $"invoices-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    /// <summary>
    /// Export invoices as a Microsoft Excel (.xlsx) file.
    /// Zero NuGet dependencies — built using raw OOXML SpreadsheetML.
    /// </summary>
    [HttpGet("export/excel")]
    public async Task<IActionResult> ExportExcel(Guid companyId, [FromQuery] ReportFilterRequest filter)
    {
        var result = await _reporting.ExportInvoicesExcelAsync(companyId, filter, _currentUser.GetUserId());
        if (!result.IsSuccess) return StatusCode(result.StatusCode, new { error = result.Error });

        const string ct = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return File(result.Data!, ct,
            $"invoices-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PDF Reports
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate a PDF monthly spend report for a specific month and year.
    /// Includes summary cards, vendor/category breakdowns, and a full invoice list.
    /// Uses QuestPDF (MIT licensed — free for revenue under $1M).
    /// </summary>
    [HttpGet("pdf/monthly-spend")]
    public async Task<IActionResult> ExportMonthlySpendPdf(
        Guid companyId, [FromQuery] MonthlySpendReportRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _pdfService.GenerateMonthlySpendReportAsync(
            companyId, request, _currentUser.GetUserId());

        if (!result.IsSuccess) return StatusCode(result.StatusCode, new { error = result.Error });

        return File(result.Data!, "application/pdf",
            $"monthly-spend-{request.Year}-{request.Month:D2}.pdf");
    }

    /// <summary>
    /// Generate a PDF VAT report for a date range.
    /// Covers net, VAT, and gross totals by rate — suitable for accountant review.
    /// </summary>
    [HttpGet("pdf/vat")]
    public async Task<IActionResult> ExportVatPdf(
        Guid companyId, [FromQuery] VatReportPdfRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _pdfService.GenerateVatReportAsync(
            companyId, request, _currentUser.GetUserId());

        if (!result.IsSuccess) return StatusCode(result.StatusCode, new { error = result.Error });

        return File(result.Data!, "application/pdf",
            $"vat-report-{request.PeriodStart:yyyyMMdd}-{request.PeriodEnd:yyyyMMdd}.pdf");
    }
}
