using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

/// <summary>
/// Converts invoice amounts between currencies using Open Exchange Rates,
/// with a DB-backed cache (Currency:CacheHours) so the free-tier quota
/// isn't burned on every request. When the API is unreachable or not
/// configured, the most recent cached rate is reused; a small static
/// table keeps development environments working offline.
/// </summary>
public class CurrencyConversionService : ICurrencyConversionService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<CurrencyConversionService> _logger;

    private int CacheHours => int.Parse(_config["Currency:CacheHours"] ?? "4");
    private string? AppId
    {
        get
        {
            var id = _config["Currency:OpenExchangeRatesAppId"];
            return string.IsNullOrWhiteSpace(id) || id.StartsWith("REPLACE_WITH") ? null : id;
        }
    }

    // Offline fallback, approximate rates against GBP — dev environments only.
    private static readonly Dictionary<string, decimal> FallbackGbpRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GBP"] = 1m, ["USD"] = 1.27m, ["EUR"] = 1.17m, ["JPY"] = 191m,
        ["CHF"] = 1.12m, ["CAD"] = 1.73m, ["AUD"] = 1.92m, ["SEK"] = 13.4m,
        ["NOK"] = 13.6m, ["DKK"] = 8.7m, ["PLN"] = 5.0m, ["INR"] = 106m
    };

    public CurrencyConversionService(
        ApplicationDbContext db,
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<CurrencyConversionService> logger)
    {
        _db = db;
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<ServiceResult<ExchangeRateResponse>> GetRateAsync(string fromCurrency, string toCurrency)
    {
        var from = NormalizeCurrency(fromCurrency);
        var to = NormalizeCurrency(toCurrency);

        if (from == null || to == null)
            return ServiceResult<ExchangeRateResponse>.Fail("Currency codes must be 3-letter ISO codes (e.g. USD, GBP).", 400);

        var rate = await ResolveRateAsync(from, to);
        if (rate == null)
            return ServiceResult<ExchangeRateResponse>.Fail($"No exchange rate available for {from}→{to}.", 404);

        return ServiceResult<ExchangeRateResponse>.Success(MapRate(rate));
    }

    public async Task<ServiceResult<CurrencyConversionResult>> ConvertAsync(string fromCurrency, string toCurrency, decimal amount)
    {
        if (amount < 0)
            return ServiceResult<CurrencyConversionResult>.Fail("Amount must be non-negative.", 400);

        var rateResult = await GetRateAsync(fromCurrency, toCurrency);
        if (!rateResult.IsSuccess || rateResult.Data == null)
            return ServiceResult<CurrencyConversionResult>.Fail(rateResult.Error ?? "Rate unavailable.", rateResult.StatusCode);

        var rate = rateResult.Data;
        return ServiceResult<CurrencyConversionResult>.Success(new CurrencyConversionResult
        {
            FromCurrency = rate.FromCurrency,
            ToCurrency = rate.ToCurrency,
            OriginalAmount = amount,
            ConvertedAmount = Math.Round(amount * rate.Rate, 2),
            Rate = rate.Rate,
            RateDate = rate.RateDate,
            Source = rate.Source
        });
    }

    public async Task AttachRateToInvoiceAsync(Guid invoiceId, string invoiceCurrency, string baseCurrency)
    {
        try
        {
            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
            if (invoice == null) return;

            var from = NormalizeCurrency(invoiceCurrency) ?? "GBP";
            var to = NormalizeCurrency(baseCurrency) ?? "GBP";

            var rate = await ResolveRateAsync(from, to);
            if (rate == null) return;

            invoice.BaseCurrency = to;
            invoice.ExchangeRate = rate.Rate;
            invoice.BaseCurrencyAmount = invoice.TotalAmount.HasValue
                ? Math.Round(invoice.TotalAmount.Value * rate.Rate, 2)
                : null;
            invoice.ExchangeRateFetchedAt = DateTime.UtcNow;
            invoice.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Currency enrichment is best-effort — never block the invoice pipeline.
            _logger.LogWarning(ex, "Failed to attach exchange rate to invoice {Id}", invoiceId);
        }
    }

    public async Task<ServiceResult<List<ExchangeRateResponse>>> GetCachedRatesAsync(string baseCurrency)
    {
        var from = NormalizeCurrency(baseCurrency);
        if (from == null)
            return ServiceResult<List<ExchangeRateResponse>>.Fail("Currency code must be a 3-letter ISO code.", 400);

        var rates = await _db.ExchangeRates
            .Where(r => r.FromCurrency == from)
            .GroupBy(r => r.ToCurrency)
            .Select(g => g.OrderByDescending(r => r.FetchedAt).First())
            .ToListAsync();

        return ServiceResult<List<ExchangeRateResponse>>.Success(
            rates.OrderBy(r => r.ToCurrency).Select(MapRate).ToList());
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task<ExchangeRate?> ResolveRateAsync(string from, string to)
    {
        if (from == to)
        {
            return new ExchangeRate
            {
                FromCurrency = from, ToCurrency = to, Rate = 1m,
                Source = "Identity", RateDate = DateTime.UtcNow.Date
            };
        }

        var cacheCutoff = DateTime.UtcNow.AddHours(-CacheHours);
        var cached = await _db.ExchangeRates
            .Where(r => r.FromCurrency == from && r.ToCurrency == to)
            .OrderByDescending(r => r.FetchedAt)
            .FirstOrDefaultAsync();

        if (cached != null && cached.FetchedAt >= cacheCutoff)
            return cached;

        var fetched = await FetchFromProviderAsync(from, to);
        if (fetched != null)
        {
            _db.ExchangeRates.Add(fetched);
            await _db.SaveChangesAsync();
            return fetched;
        }

        // Stale cache beats no rate at all.
        if (cached != null)
        {
            _logger.LogWarning("Using stale exchange rate for {From}→{To} fetched at {At}", from, to, cached.FetchedAt);
            return cached;
        }

        return StaticFallbackRate(from, to);
    }

    private async Task<ExchangeRate?> FetchFromProviderAsync(string from, string to)
    {
        if (AppId == null) return null;

        try
        {
            var client = _http.CreateClient("OxrClient");
            client.Timeout = TimeSpan.FromSeconds(10);

            // Free tier only serves USD-based rates; derive the cross rate.
            var url = $"https://openexchangerates.org/api/latest.json?app_id={AppId}&symbols={from},{to}";
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenExchangeRates returned {Status} for {From}→{To}", response.StatusCode, from, to);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rates = doc.RootElement.GetProperty("rates");

            decimal UsdTo(string code) => code == "USD" ? 1m : rates.GetProperty(code).GetDecimal();

            var usdToFrom = UsdTo(from);
            var usdToTo = UsdTo(to);
            if (usdToFrom == 0) return null;

            return new ExchangeRate
            {
                FromCurrency = from,
                ToCurrency = to,
                Rate = Math.Round(usdToTo / usdToFrom, 6),
                Source = "OpenExchangeRates",
                RateDate = DateTimeOffset.FromUnixTimeSeconds(doc.RootElement.GetProperty("timestamp").GetInt64()).UtcDateTime.Date,
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exchange rate fetch failed for {From}→{To}", from, to);
            return null;
        }
    }

    private ExchangeRate? StaticFallbackRate(string from, string to)
    {
        if (!FallbackGbpRates.TryGetValue(from, out var gbpToFrom) ||
            !FallbackGbpRates.TryGetValue(to, out var gbpToTo) ||
            gbpToFrom == 0)
            return null;

        _logger.LogWarning("Using static fallback exchange rate for {From}→{To} — configure Currency:OpenExchangeRatesAppId.", from, to);

        return new ExchangeRate
        {
            FromCurrency = from,
            ToCurrency = to,
            Rate = Math.Round(gbpToTo / gbpToFrom, 6),
            Source = "StaticFallback",
            RateDate = DateTime.UtcNow.Date
        };
    }

    private static string? NormalizeCurrency(string? code)
    {
        var c = code?.Trim().ToUpperInvariant();
        return c?.Length == 3 && c.All(char.IsLetter) ? c : null;
    }

    private static ExchangeRateResponse MapRate(ExchangeRate r) => new()
    {
        FromCurrency = r.FromCurrency,
        ToCurrency = r.ToCurrency,
        Rate = r.Rate,
        RateDate = r.RateDate,
        Source = r.Source,
        FetchedAt = r.FetchedAt
    };
}
