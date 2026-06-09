using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

/// <summary>
/// Reporting &amp; exports — spend analytics, VAT summary, CSV/Excel downloads.
/// All filters are optional query parameters (from, to, currency, vendorId,
/// categoryId, status).
/// </summary>
[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/reports")]
public class ReportingController : ControllerBase
{
    private readonly IReportingService _reporting;
    private readonly CurrentUserProvider _currentUser;

    public ReportingController(IReportingService reporting, CurrentUserProvider currentUser)
    {
        _reporting = reporting;
        _currentUser = currentUser;
    }

    [HttpGet("spend-analytics")]
    public async Task<IActionResult> SpendAnalytics(Guid companyId, [FromQuery] ReportFilterRequest filter)
    {
        var result = await _reporting.GetSpendAnalyticsAsync(companyId, filter, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("vat-summary")]
    public async Task<IActionResult> VatSummary(Guid companyId, [FromQuery] DateTime periodStart, [FromQuery] DateTime periodEnd)
    {
        var result = await _reporting.GetVatSummaryAsync(companyId, periodStart, periodEnd, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv(Guid companyId, [FromQuery] ReportFilterRequest filter)
    {
        var result = await _reporting.ExportInvoicesCsvAsync(companyId, filter, _currentUser.GetUserId());
        return result.IsSuccess
            ? File(result.Data!, "text/csv", $"invoices-{DateTime.UtcNow:yyyyMMdd}.csv")
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet("export/excel")]
    public async Task<IActionResult> ExportExcel(Guid companyId, [FromQuery] ReportFilterRequest filter)
    {
        var result = await _reporting.ExportInvoicesExcelAsync(companyId, filter, _currentUser.GetUserId());
        return result.IsSuccess
            ? File(result.Data!,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"invoices-{DateTime.UtcNow:yyyyMMdd}.xlsx")
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
