using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/currency")]
public class CurrencyController : ControllerBase
{
    private readonly ICurrencyConversionService _service;
    private readonly CurrentUserProvider _currentUser;

    public CurrencyController(ICurrencyConversionService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>Get the company's base currency setting.</summary>
    [HttpGet("base")]
    public async Task<IActionResult> GetBaseCurrency(Guid companyId)
    {
        var result = await _service.GetBaseCurrencyAsync(companyId);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Set the company's base currency (e.g. "GBP").</summary>
    [HttpPut("base")]
    public async Task<IActionResult> SetBaseCurrency(Guid companyId, [FromBody] SetBaseCurrencyRequest request)
    {
        var result = await _service.SetBaseCurrencyAsync(companyId, request);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Convert an invoice's amount into the company base currency.</summary>
    [HttpPost("invoices/{invoiceId:guid}/convert")]
    public async Task<IActionResult> ConvertInvoice(Guid companyId, Guid invoiceId, [FromBody] ConvertInvoiceRequest request)
    {
        var result = await _service.ConvertInvoiceAsync(invoiceId, companyId, request);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Get the stored conversion record for an invoice.</summary>
    [HttpGet("invoices/{invoiceId:guid}")]
    public async Task<IActionResult> GetConversion(Guid companyId, Guid invoiceId)
    {
        var result = await _service.GetConversionAsync(invoiceId);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>List cached exchange rates for the company's base currency.</summary>
    [HttpGet("rates")]
    public async Task<IActionResult> GetRates(Guid companyId)
    {
        var result = await _service.GetRatesAsync(companyId);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Monthly spend summary broken down by currency.</summary>
    [HttpGet("summary/{year:int}/{month:int}")]
    public async Task<IActionResult> GetSummary(Guid companyId, int year, int month)
    {
        var result = await _service.GetCurrencySummaryAsync(companyId, year, month);
        return result.IsSuccess ? Ok(result.Data) : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
