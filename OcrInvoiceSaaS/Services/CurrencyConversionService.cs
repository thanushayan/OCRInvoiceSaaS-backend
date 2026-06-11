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

    // ── Base Currency Setting ─────────────────────────────────────────────────

    public async Task<ServiceResult<CompanyCurrencySettingResponse>> GetBaseCurrencyAsync(Guid companyId)
    {
        var setting = await _db.CompanyCurrencySettings
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);

        if (setting == null)
        {
            return ServiceResult<CompanyCurrencySettingResponse>.Success(new CompanyCurrencySettingResponse
            {
                CompanyId    = companyId,
                BaseCurrency = "USD",
                UpdatedAt    = DateTime.UtcNow
            });
        }

        return ServiceResult<CompanyCurrencySettingResponse>.Success(MapSetting(setting));
    }

    public async Task<ServiceResult<CompanyCurrencySettingResponse>> SetBaseCurrencyAsync(
        Guid companyId, SetBaseCurrencyRequest request)
    {
        var setting = await _db.CompanyCurrencySettings
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);

        if (setting == null)
        {
            setting = new CompanyCurrencySetting
            {
                CompanyId    = companyId,
                BaseCurrency = request.BaseCurrency.ToUpper(),
                UpdatedAt    = DateTime.UtcNow
            };
            _db.CompanyCurrencySettings.Add(setting);
        }
        else
        {
            setting.BaseCurrency = request.BaseCurrency.ToUpper();
            setting.UpdatedAt    = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return ServiceResult<CompanyCurrencySettingResponse>.Success(MapSetting(setting));
    }

    // ── Invoice Conversion ────────────────────────────────────────────────────

    public async Task<ServiceResult<InvoiceCurrencyConversionResponse>> ConvertInvoiceAsync(
        Guid invoiceId, Guid companyId, ConvertInvoiceRequest request)
    {
        var invoice = await _db.Invoices.FindAsync(invoiceId);
        if (invoice == null)
            return ServiceResult<InvoiceCurrencyConversionResponse>.Fail("Invoice not found.", 404);

        var baseCurrencyResult = await GetBaseCurrencyAsync(companyId);
        var baseCurrency = baseCurrencyResult.Data!.BaseCurrency;
        var fromCurrency = request.OriginalCurrency.ToUpper();

        // Remove existing conversion if re-converting
        var existing = await _db.InvoiceCurrencyConversions
            .FirstOrDefaultAsync(c => c.InvoiceId == invoiceId);
        if (existing != null)
            _db.InvoiceCurrencyConversions.Remove(existing);

        decimal rate;
        bool isManual = false;
        Guid? exchangeRateId = null;

        if (request.ManualRate.HasValue && request.ManualRate.Value > 0)
        {
            rate     = request.ManualRate.Value;
            isManual = true;
        }
        else if (fromCurrency == baseCurrency)
        {
            rate = 1m;
        }
        else
        {
            var cached = await _db.ExchangeRates
                .Where(r => r.FromCurrency == fromCurrency && r.ToCurrency == baseCurrency)
                .OrderByDescending(r => r.FetchedAt)
                .FirstOrDefaultAsync();

            if (cached == null)
            {
                cached = await FetchAndSaveRateAsync(fromCurrency, baseCurrency);
                if (cached == null)
                    return ServiceResult<InvoiceCurrencyConversionResponse>.Fail(
                        $"Could not retrieve exchange rate for {fromCurrency} to {baseCurrency}.", 422);
            }

            rate           = cached.Rate;
            exchangeRateId = cached.Id;
        }

        var conversion = new InvoiceCurrencyConversion
        {
            InvoiceId        = invoiceId,
            OriginalCurrency = fromCurrency,
            OriginalAmount   = request.OriginalAmount,
            BaseCurrency     = baseCurrency,
            ConvertedAmount  = Math.Round(request.OriginalAmount * rate, 2),
            RateUsed         = rate,
            IsManualRate     = isManual,
            ExchangeRateId   = exchangeRateId,
            ConvertedAt      = DateTime.UtcNow
        };

        _db.InvoiceCurrencyConversions.Add(conversion);
        await _db.SaveChangesAsync();

        return ServiceResult<InvoiceCurrencyConversionResponse>.Success(MapConversion(conversion));
    }

    public async Task<ServiceResult<InvoiceCurrencyConversionResponse>> GetConversionAsync(Guid invoiceId)
    {
        var conversion = await _db.InvoiceCurrencyConversions
            .FirstOrDefaultAsync(c => c.InvoiceId == invoiceId);

        if (conversion == null)
            return ServiceResult<InvoiceCurrencyConversionResponse>.Fail(
                "No conversion record found for this invoice.", 404);

        return ServiceResult<InvoiceCurrencyConversionResponse>.Success(MapConversion(conversion));
    }

    // ── Exchange Rates ────────────────────────────────────────────────────────

    public async Task<ServiceResult<List<ExchangeRateResponse>>> GetRatesAsync(Guid companyId)
    {
        var baseCurrencyResult = await GetBaseCurrencyAsync(companyId);
        var baseCurrency = baseCurrencyResult.Data!.BaseCurrency;

        var rates = await _db.ExchangeRates
            .Where(r => r.ToCurrency == baseCurrency)
            .OrderBy(r => r.FromCurrency)
            .ToListAsync();

        var result = rates.Select(r => new ExchangeRateResponse
        {
            Id           = r.Id,
            FromCurrency = r.FromCurrency,
            ToCurrency   = r.ToCurrency,
            Rate         = r.Rate,
            Source       = r.Source,
            RateDate     = r.RateDate,
            FetchedAt    = r.FetchedAt
        }).ToList();

        return ServiceResult<List<ExchangeRateResponse>>.Success(result);
    }

    public async Task RefreshRatesAsync()
    {
        var baseCurrencies = await _db.CompanyCurrencySettings
            .Select(s => s.BaseCurrency)
            .Distinct()
            .ToListAsync();

        if (!baseCurrencies.Any())
            baseCurrencies = new List<string> { "USD" };

        var commonCurrencies = new[] { "USD", "EUR", "GBP", "JPY", "CAD", "AUD", "CHF", "CNY", "INR", "SGD" };

        foreach (var baseCurrency in baseCurrencies)
        {
            foreach (var from in commonCurrencies)
            {
                if (from == baseCurrency) continue;

                try { await FetchAndSaveRateAsync(from, baseCurrency); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh rate {From} to {To}", from, baseCurrency);
                }
            }
        }
    }

    // ── Currency Summary ──────────────────────────────────────────────────────

    public async Task<ServiceResult<CurrencySummaryResponse>> GetCurrencySummaryAsync(
        Guid companyId, int year, int month)
    {
        var baseCurrencyResult = await GetBaseCurrencyAsync(companyId);
        var baseCurrency = baseCurrencyResult.Data!.BaseCurrency;

        var conversions = await _db.InvoiceCurrencyConversions
            .Include(c => c.Invoice)
            .Where(c => c.Invoice.CompanyId == companyId
                     && c.ConvertedAt.Year  == year
                     && c.ConvertedAt.Month == month)
            .ToListAsync();

        var breakdown = conversions
            .GroupBy(c => c.OriginalCurrency)
            .Select(g => new CurrencyBreakdownItem
            {
                Currency             = g.Key,
                InvoiceCount         = g.Count(),
                TotalOriginalAmount  = g.Sum(c => c.OriginalAmount),
                TotalConvertedAmount = g.Sum(c => c.ConvertedAmount),
                AverageRate          = g.Average(c => c.RateUsed)
            })
            .OrderByDescending(b => b.TotalConvertedAmount)
            .ToList();

        return ServiceResult<CurrencySummaryResponse>.Success(new CurrencySummaryResponse
        {
            BaseCurrency        = baseCurrency,
            Breakdown           = breakdown,
            TotalInBaseCurrency = breakdown.Sum(b => b.TotalConvertedAmount)
        });
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private async Task<ExchangeRate?> FetchAndSaveRateAsync(string from, string to)
    {
        var apiKey  = _config["ExchangeRate:ApiKey"];
        var baseUrl = _config["ExchangeRate:BaseUrl"] ?? "https://v6.exchangerate-api.com/v6";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("ExchangeRate:ApiKey not configured.");
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("ExchangeRate");
            var url    = $"{baseUrl}/{apiKey}/pair/{from}/{to}";
            var resp   = await client.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            if (root.GetProperty("result").GetString() != "success") return null;

            var rate = root.GetProperty("conversion_rate").GetDecimal();

            var existing = await _db.ExchangeRates
                .FirstOrDefaultAsync(r => r.FromCurrency == from && r.ToCurrency == to);

            if (existing != null)
            {
                existing.Rate      = rate;
                existing.RateDate  = DateTime.UtcNow.Date;
                existing.FetchedAt = DateTime.UtcNow;
                existing.Source    = "ExchangeRate-API";
            }
            else
            {
                existing = new ExchangeRate
                {
                    FromCurrency = from,
                    ToCurrency   = to,
                    Rate         = rate,
                    RateDate     = DateTime.UtcNow.Date,
                    FetchedAt    = DateTime.UtcNow,
                    Source       = "ExchangeRate-API"
                };
                _db.ExchangeRates.Add(existing);
            }

            await _db.SaveChangesAsync();
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch exchange rate {From} to {To}", from, to);
            return null;
        }
    }

    private static CompanyCurrencySettingResponse MapSetting(CompanyCurrencySetting s) => new()
    {
        CompanyId    = s.CompanyId,
        BaseCurrency = s.BaseCurrency,
        UpdatedAt    = s.UpdatedAt
    };

    private static InvoiceCurrencyConversionResponse MapConversion(InvoiceCurrencyConversion c) => new()
    {
        Id               = c.Id,
        InvoiceId        = c.InvoiceId,
        OriginalCurrency = c.OriginalCurrency,
        OriginalAmount   = c.OriginalAmount,
        BaseCurrency     = c.BaseCurrency,
        ConvertedAmount  = c.ConvertedAmount,
        RateUsed         = c.RateUsed,
        IsManualRate     = c.IsManualRate,
        ConvertedAt      = c.ConvertedAt
    };
}
