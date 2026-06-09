using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class CurrencyConversionService : ICurrencyConversionService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<CurrencyConversionService> _logger;

    // Rates are cached for 4 hours before re-fetching
    private const int CacheHours = 4;

    public CurrencyConversionService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<CurrencyConversionService> logger)
    {
        _db                = db;
        _httpClientFactory = httpClientFactory;
        _config            = config;
        _logger            = logger;
    }

    public async Task<ServiceResult<ExchangeRateResponse>> GetRateAsync(string fromCurrency, string toCurrency)
    {
        var from = fromCurrency.ToUpper().Trim();
        var to   = toCurrency.ToUpper().Trim();

        if (from == to)
        {
            return ServiceResult<ExchangeRateResponse>.Success(new ExchangeRateResponse
            {
                FromCurrency = from,
                ToCurrency   = to,
                Rate         = 1m,
                RateDate     = DateTime.UtcNow.Date,
                Source       = "Identity",
                FetchedAt    = DateTime.UtcNow
            });
        }

        // Check cache first
        var cached = await _db.ExchangeRates
            .Where(r => r.FromCurrency == from && r.ToCurrency == to &&
                        r.FetchedAt >= DateTime.UtcNow.AddHours(-CacheHours))
            .OrderByDescending(r => r.FetchedAt)
            .FirstOrDefaultAsync();

        if (cached != null)
        {
            return ServiceResult<ExchangeRateResponse>.Success(new ExchangeRateResponse
            {
                FromCurrency = cached.FromCurrency,
                ToCurrency   = cached.ToCurrency,
                Rate         = cached.Rate,
                RateDate     = cached.RateDate,
                Source       = cached.Source,
                FetchedAt    = cached.FetchedAt
            });
        }

        // Fetch from external API
        var rate = await FetchRateFromApiAsync(from, to);
        if (rate == null)
        {
            // Fall back to manual/stored rate if API fails
            var fallback = await _db.ExchangeRates
                .Where(r => r.FromCurrency == from && r.ToCurrency == to)
                .OrderByDescending(r => r.FetchedAt)
                .FirstOrDefaultAsync();

            if (fallback != null)
            {
                _logger.LogWarning("Using stale rate for {From}/{To} — API unavailable.", from, to);
                return ServiceResult<ExchangeRateResponse>.Success(new ExchangeRateResponse
                {
                    FromCurrency = fallback.FromCurrency,
                    ToCurrency   = fallback.ToCurrency,
                    Rate         = fallback.Rate,
                    RateDate     = fallback.RateDate,
                    Source       = $"{fallback.Source} (stale)",
                    FetchedAt    = fallback.FetchedAt
                });
            }

            return ServiceResult<ExchangeRateResponse>.Fail(
                $"Could not fetch exchange rate for {from}/{to}. Please enter a manual rate.", 503);
        }

        // Persist fresh rate
        var record = new ExchangeRate
        {
            FromCurrency = from,
            ToCurrency   = to,
            Rate         = rate.Value,
            Source       = _config["Currency:Provider"] ?? "OpenExchangeRates",
            RateDate     = DateTime.UtcNow.Date
        };

        _db.ExchangeRates.Add(record);
        await _db.SaveChangesAsync();

        return ServiceResult<ExchangeRateResponse>.Success(new ExchangeRateResponse
        {
            FromCurrency = from,
            ToCurrency   = to,
            Rate         = rate.Value,
            RateDate     = record.RateDate,
            Source       = record.Source,
            FetchedAt    = record.FetchedAt
        });
    }

    public async Task<ServiceResult<CurrencyConversionResult>> ConvertAsync(
        string fromCurrency, string toCurrency, decimal amount)
    {
        var rateResult = await GetRateAsync(fromCurrency, toCurrency);
        if (!rateResult.IsSuccess)
            return ServiceResult<CurrencyConversionResult>.Fail(rateResult.Error!, rateResult.StatusCode);

        var r = rateResult.Data!;
        return ServiceResult<CurrencyConversionResult>.Success(new CurrencyConversionResult
        {
            FromCurrency    = r.FromCurrency,
            ToCurrency      = r.ToCurrency,
            OriginalAmount  = amount,
            ConvertedAmount = Math.Round(amount * r.Rate, 2),
            Rate            = r.Rate,
            RateDate        = r.RateDate,
            Source          = r.Source
        });
    }

    public async Task AttachRateToInvoiceAsync(Guid invoiceId, string invoiceCurrency, string baseCurrency)
    {
        var invoice = await _db.Invoices.FindAsync(invoiceId);
        if (invoice == null) return;

        var from = invoiceCurrency.ToUpper().Trim();
        var to   = baseCurrency.ToUpper().Trim();

        invoice.BaseCurrency = to;

        if (from == to)
        {
            invoice.ExchangeRate         = 1m;
            invoice.BaseCurrencyAmount   = invoice.TotalAmount;
            invoice.ExchangeRateFetchedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return;
        }

        var rateResult = await GetRateAsync(from, to);
        if (rateResult.IsSuccess && rateResult.Data != null)
        {
            invoice.ExchangeRate          = rateResult.Data.Rate;
            invoice.BaseCurrencyAmount    = invoice.TotalAmount.HasValue
                ? Math.Round(invoice.TotalAmount.Value * rateResult.Data.Rate, 2)
                : null;
            invoice.ExchangeRateFetchedAt = DateTime.UtcNow;
        }
        else
        {
            _logger.LogWarning("Failed to attach rate to invoice {Id}: {Err}", invoiceId, rateResult.Error);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<ServiceResult<List<ExchangeRateResponse>>> GetCachedRatesAsync(string baseCurrency)
    {
        var to   = baseCurrency.ToUpper().Trim();
        var cutoff = DateTime.UtcNow.AddHours(-CacheHours);

        var rates = await _db.ExchangeRates
            .Where(r => r.ToCurrency == to && r.FetchedAt >= cutoff)
            .GroupBy(r => r.FromCurrency)
            .Select(g => g.OrderByDescending(r => r.FetchedAt).First())
            .ToListAsync();

        return ServiceResult<List<ExchangeRateResponse>>.Success(rates.Select(r => new ExchangeRateResponse
        {
            FromCurrency = r.FromCurrency,
            ToCurrency   = r.ToCurrency,
            Rate         = r.Rate,
            RateDate     = r.RateDate,
            Source       = r.Source,
            FetchedAt    = r.FetchedAt
        }).ToList());
    }

    // ── External API integration ──────────────────────────────────────────────
    // Uses Open Exchange Rates (free tier). Swap with ECB, Fixer.io, etc.
    // Set "Currency:OpenExchangeRatesAppId" in appsettings.json.
    // If no API key is set, returns null → graceful fallback to stale cache.

    private async Task<decimal?> FetchRateFromApiAsync(string from, string to)
    {
        var appId = _config["Currency:OpenExchangeRatesAppId"];
        if (string.IsNullOrWhiteSpace(appId))
        {
            _logger.LogDebug("No Currency:OpenExchangeRatesAppId configured — using cached/manual rates only.");
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("OxrClient");
            // OXR returns rates relative to USD base on free plan
            var url = $"https://openexchangerates.org/api/latest.json?app_id={appId}&base=USD&symbols={from},{to}";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rates = json.RootElement.GetProperty("rates");

            decimal fromRate = from == "USD" ? 1m : rates.GetProperty(from).GetDecimal();
            decimal toRate   = to == "USD"   ? 1m : rates.GetProperty(to).GetDecimal();

            // Convert: 1 FROM = (toRate / fromRate) TO
            return Math.Round(toRate / fromRate, 6);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exchange rate API call failed for {From}/{To}", from, to);
            return null;
        }
    }
}
