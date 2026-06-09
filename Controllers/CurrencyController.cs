using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

/// <summary>
/// Feature 4 — Currency Conversion.
/// Rates come from Open Exchange Rates with a DB cache
/// (Currency:CacheHours); conversions are rounded to 2 dp.
/// </summary>
[Authorize]
[ApiController]
[Route("api")]
public class CurrencyController : ControllerBase
{
    private readonly ICurrencyConversionService _currency;
    private readonly IInvoiceService _invoices;
    private readonly CurrentUserProvider _currentUser;
    private readonly IConfiguration _config;

    public CurrencyController(
        ICurrencyConversionService currency,
        IInvoiceService invoices,
        CurrentUserProvider currentUser,
        IConfiguration config)
    {
        _currency = currency;
        _invoices = invoices;
        _currentUser = currentUser;
        _config = config;
    }

    /// <summary>Current exchange rate for a currency pair, e.g. ?from=USD&amp;to=GBP.</summary>
    [HttpGet("currency/rate")]
    public async Task<IActionResult> GetRate([FromQuery] string from, [FromQuery] string to)
    {
        var result = await _currency.GetRateAsync(from, to);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Converts an amount between currencies, e.g. ?from=USD&amp;to=GBP&amp;amount=100.</summary>
    [HttpGet("currency/convert")]
    public async Task<IActionResult> Convert([FromQuery] string from, [FromQuery] string to, [FromQuery] decimal amount)
    {
        var result = await _currency.ConvertAsync(from, to, amount);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Latest cached rate per pair for a base currency (default GBP).</summary>
    [HttpGet("currency/rates")]
    public async Task<IActionResult> GetCachedRates([FromQuery(Name = "base")] string? baseCurrency)
    {
        var result = await _currency.GetCachedRatesAsync(
            baseCurrency ?? _config["Currency:BaseCurrency"] ?? "GBP");
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// (Re)attaches the current exchange rate to an invoice, recalculating
    /// its base-currency amount. Useful after editing the total or currency.
    /// </summary>
    [HttpPost("invoices/{invoiceId:guid}/exchange-rate/refresh")]
    public async Task<IActionResult> RefreshInvoiceRate(Guid invoiceId)
    {
        var userId = _currentUser.GetUserId();

        // Membership gate — AttachRateToInvoiceAsync itself is tenant-blind.
        var invoice = await _invoices.GetInvoiceByIdAsync(invoiceId, userId);
        if (!invoice.IsSuccess || invoice.Data == null)
            return StatusCode(invoice.StatusCode, new { error = invoice.Error });

        var baseCurrency = _config["Currency:BaseCurrency"] ?? "GBP";
        await _currency.AttachRateToInvoiceAsync(invoiceId, invoice.Data.Currency ?? "GBP", baseCurrency);

        var refreshed = await _invoices.GetInvoiceByIdAsync(invoiceId, userId);
        return Ok(refreshed.Data);
    }
}
