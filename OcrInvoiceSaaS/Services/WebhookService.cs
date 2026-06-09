using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class WebhookService : IWebhookService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookService> _logger;

    // Exponential back-off delays for retries: 30s, 5m, 30m, 2h, 8h
    private static readonly int[] RetryDelayMinutes = [0, 5, 30, 120, 480];
    private const int MaxAttempts = 5;

    public WebhookService(ApplicationDbContext db, IHttpClientFactory httpClientFactory, ILogger<WebhookService> logger)
    {
        _db                = db;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    // ── Endpoint management ───────────────────────────────────────────────────

    public async Task<ServiceResult<CreateWebhookResponse>> CreateEndpointAsync(
        Guid companyId, CreateWebhookRequest request, Guid userId)
    {
        bool isAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == userId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isAdmin) return ServiceResult<CreateWebhookResponse>.Fail("Only owners and admins can manage webhooks.", 403);

        // Validate subscribed event names
        var validEvents = Enum.GetNames<WebhookEvent>();
        var invalidEvents = request.Events.Where(e => !validEvents.Contains(e)).ToList();
        if (invalidEvents.Count > 0)
            return ServiceResult<CreateWebhookResponse>.Fail($"Unknown events: {string.Join(", ", invalidEvents)}", 400);

        var rawSecret = SecurityHelper.GenerateSecureToken(32);
        var secretHash = SecurityHelper.HashToken(rawSecret);
        var secretPrefix = rawSecret[..4];

        var endpoint = new WebhookEndpoint
        {
            CompanyId    = companyId,
            Url          = request.Url.Trim(),
            Description  = request.Description.Trim(),
            SecretHash   = secretHash,
            SecretPrefix = secretPrefix,
            Events       = request.Events.Distinct().ToArray()
        };

        _db.WebhookEndpoints.Add(endpoint);
        await _db.SaveChangesAsync();

        return ServiceResult<CreateWebhookResponse>.Success(new CreateWebhookResponse
        {
            Id            = endpoint.Id,
            Url           = endpoint.Url,
            Description   = endpoint.Description,
            SigningSecret = rawSecret,              // shown once
            Events        = endpoint.Events.ToList(),
            CreatedAt     = endpoint.CreatedAt
        }, 201);
    }

    public async Task<ServiceResult<List<WebhookResponse>>> GetEndpointsAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<WebhookResponse>>.Fail("Access denied.", 403);

        var endpoints = await _db.WebhookEndpoints
            .Where(e => e.CompanyId == companyId && e.IsActive)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new WebhookResponse
            {
                Id               = e.Id,
                Url              = e.Url,
                Description      = e.Description,
                SecretPrefix     = e.SecretPrefix,
                Events           = e.Events.ToList(),
                IsActive         = e.IsActive,
                TotalDeliveries  = e.TotalDeliveries,
                FailedDeliveries = e.FailedDeliveries,
                LastTriggeredAt  = e.LastTriggeredAt,
                CreatedAt        = e.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<WebhookResponse>>.Success(endpoints);
    }

    public async Task<ServiceResult> DeleteEndpointAsync(Guid endpointId, Guid userId)
    {
        var endpoint = await _db.WebhookEndpoints
            .Include(e => e.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(e => e.Id == endpointId);

        if (endpoint == null) return ServiceResult.Fail("Webhook endpoint not found.", 404);

        bool isAdmin = endpoint.Company.CompanyUsers.Any(cu =>
            cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isAdmin) return ServiceResult.Fail("Only owners and admins can delete webhooks.", 403);

        endpoint.IsActive = false;
        await _db.SaveChangesAsync();
        return ServiceResult.Success(204);
    }

    public async Task<ServiceResult<List<WebhookDeliveryResponse>>> GetDeliveriesAsync(Guid endpointId, Guid userId)
    {
        var endpoint = await _db.WebhookEndpoints
            .Include(e => e.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(e => e.Id == endpointId);

        if (endpoint == null) return ServiceResult<List<WebhookDeliveryResponse>>.Fail("Endpoint not found.", 404);
        bool isMember = endpoint.Company.CompanyUsers.Any(cu => cu.UserId == userId);
        if (!isMember) return ServiceResult<List<WebhookDeliveryResponse>>.Fail("Access denied.", 403);

        var deliveries = await _db.WebhookDeliveries
            .Where(d => d.WebhookEndpointId == endpointId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(100)
            .Select(d => new WebhookDeliveryResponse
            {
                Id                 = d.Id,
                EventName          = d.EventName,
                Status             = d.Status.ToString(),
                AttemptCount       = d.AttemptCount,
                ResponseStatusCode = d.ResponseStatusCode,
                ErrorMessage       = d.ErrorMessage,
                DeliveredAt        = d.DeliveredAt,
                CreatedAt          = d.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<WebhookDeliveryResponse>>.Success(deliveries);
    }

    public async Task<ServiceResult> RetryDeliveryAsync(Guid deliveryId, Guid userId)
    {
        var delivery = await _db.WebhookDeliveries
            .Include(d => d.WebhookEndpoint).ThenInclude(e => e.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(d => d.Id == deliveryId);

        if (delivery == null) return ServiceResult.Fail("Delivery not found.", 404);
        bool isMember = delivery.WebhookEndpoint.Company.CompanyUsers.Any(cu => cu.UserId == userId);
        if (!isMember) return ServiceResult.Fail("Access denied.", 403);

        delivery.Status      = WebhookDeliveryStatus.Pending;
        delivery.NextRetryAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult.Success();
    }

    // ── Event dispatch ────────────────────────────────────────────────────────

    public async Task DispatchEventAsync(Guid companyId, WebhookEvent eventType, object payload)
    {
        var eventName = eventType.ToString();

        var endpoints = await _db.WebhookEndpoints
            .Where(e => e.CompanyId == companyId && e.IsActive)
            .ToListAsync();

        // Filter endpoints subscribed to this event
        var targets = endpoints.Where(e =>
            e.Events.Length == 0 ||                           // subscribed to all
            e.Events.Contains(eventName)).ToList();

        if (targets.Count == 0) return;

        var payloadJson = JsonSerializer.Serialize(new
        {
            @event    = eventName,
            timestamp = DateTime.UtcNow,
            data      = payload
        });

        foreach (var endpoint in targets)
        {
            _db.WebhookDeliveries.Add(new WebhookDelivery
            {
                WebhookEndpointId = endpoint.Id,
                EventName         = eventName,
                Payload           = payloadJson,
                Status            = WebhookDeliveryStatus.Pending,
                NextRetryAt       = DateTime.UtcNow
            });

            endpoint.LastTriggeredAt = DateTime.UtcNow;
            endpoint.TotalDeliveries++;
        }

        await _db.SaveChangesAsync();
    }

    // ── Delivery processor (called by Quartz) ─────────────────────────────────

    public async Task ProcessPendingDeliveriesAsync()
    {
        var due = await _db.WebhookDeliveries
            .Include(d => d.WebhookEndpoint)
            .Where(d =>
                (d.Status == WebhookDeliveryStatus.Pending || d.Status == WebhookDeliveryStatus.Retrying) &&
                d.AttemptCount < MaxAttempts &&
                d.NextRetryAt <= DateTime.UtcNow &&
                d.WebhookEndpoint.IsActive)
            .Take(50)
            .ToListAsync();

        _logger.LogInformation("WebhookService: delivering {Count} pending events.", due.Count);

        foreach (var delivery in due)
        {
            await AttemptDeliveryAsync(delivery);
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task AttemptDeliveryAsync(WebhookDelivery delivery)
    {
        delivery.AttemptCount++;
        delivery.Status = WebhookDeliveryStatus.Retrying;

        try
        {
            var client = _httpClientFactory.CreateClient("WebhookClient");
            client.Timeout = TimeSpan.FromSeconds(10);

            var content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json");

            // HMAC-SHA256 signature header
            var signature = ComputeSignature(delivery.Payload, delivery.WebhookEndpoint.SecretHash);
            content.Headers.Add("X-OcrInvoice-Signature", $"sha256={signature}");
            content.Headers.Add("X-OcrInvoice-Event", delivery.EventName);
            content.Headers.Add("X-OcrInvoice-DeliveryId", delivery.Id.ToString());

            var response = await client.PostAsync(delivery.WebhookEndpoint.Url, content);

            delivery.ResponseStatusCode = (int)response.StatusCode;
            delivery.ResponseBody       = (await response.Content.ReadAsStringAsync())[..Math.Min(500, (int)response.Content.Headers.ContentLength.GetValueOrDefault(0))];

            if (response.IsSuccessStatusCode)
            {
                delivery.Status      = WebhookDeliveryStatus.Succeeded;
                delivery.DeliveredAt = DateTime.UtcNow;
            }
            else
            {
                ScheduleRetry(delivery);
            }
        }
        catch (Exception ex)
        {
            delivery.ErrorMessage = ex.Message[..Math.Min(500, ex.Message.Length)];
            ScheduleRetry(delivery);
            _logger.LogWarning("Webhook delivery {Id} failed: {Error}", delivery.Id, ex.Message);
        }

        if (delivery.Status == WebhookDeliveryStatus.Failed)
        {
            delivery.WebhookEndpoint.FailedDeliveries++;
        }

        await _db.SaveChangesAsync();
    }

    private static void ScheduleRetry(WebhookDelivery delivery)
    {
        if (delivery.AttemptCount >= MaxAttempts)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            return;
        }

        var delayMinutes = RetryDelayMinutes[Math.Min(delivery.AttemptCount, RetryDelayMinutes.Length - 1)];
        delivery.NextRetryAt = DateTime.UtcNow.AddMinutes(delayMinutes);
        delivery.Status      = WebhookDeliveryStatus.Retrying;
    }

    private static string ComputeSignature(string payload, string secretHash)
    {
        // We use the stored hash as the HMAC key (not the raw secret, which we don't store)
        var keyBytes = Encoding.UTF8.GetBytes(secretHash);
        var msgBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(msgBytes)).ToLowerInvariant();
    }
}
