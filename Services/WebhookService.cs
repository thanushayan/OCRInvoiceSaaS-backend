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

/// <summary>
/// Outbound webhooks: companies register HTTPS endpoints and receive signed
/// POSTs when invoice events occur.
///
/// Delivery is queue-based: DispatchEventAsync only enqueues WebhookDelivery
/// rows; WebhookRetryJob drains the queue every 5 minutes with exponential
/// backoff (5 → 10 → 20 → 40 min) up to Webhooks:MaxRetryAttempts.
///
/// Signing: the raw secret (whsec_…) is shown once at creation and only its
/// SHA-256 hash is stored. Each request carries
///   X-Webhook-Signature: t=&lt;unix-seconds&gt;,v1=&lt;hex&gt;
/// where v1 = HMAC-SHA256(key = SHA-256(rawSecret) hex, message = "{t}.{body}").
/// Receivers hash their stored secret the same way to verify.
/// </summary>
public class WebhookService : IWebhookService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<WebhookService> _logger;

    private const int DeliveryBatchSize = 25;

    private int MaxEndpointsPerCompany => int.Parse(_config["Webhooks:MaxEndpointsPerCompany"] ?? "10");
    private int DeliveryTimeoutSeconds => int.Parse(_config["Webhooks:DeliveryTimeoutSeconds"] ?? "10");
    private int MaxRetryAttempts => int.Parse(_config["Webhooks:MaxRetryAttempts"] ?? "5");

    public WebhookService(
        ApplicationDbContext db,
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<WebhookService> logger)
    {
        _db = db;
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<ServiceResult<CreateWebhookResponse>> CreateEndpointAsync(
        Guid companyId, CreateWebhookRequest request, Guid userId)
    {
        bool isAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == userId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isAdmin)
            return ServiceResult<CreateWebhookResponse>.Fail("Only owners and admins can manage webhooks.", 403);

        var url = request.Url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return ServiceResult<CreateWebhookResponse>.Fail("Webhook URL must be an absolute http(s) URL.", 400);

        var invalidEvents = request.Events
            .Where(ev => !Enum.TryParse<WebhookEvent>(ev, true, out _))
            .ToList();
        if (invalidEvents.Count > 0)
            return ServiceResult<CreateWebhookResponse>.Fail(
                $"Unknown events: {string.Join(", ", invalidEvents)}. " +
                $"Valid: {string.Join(", ", Enum.GetNames<WebhookEvent>())}.", 400);

        int existing = await _db.WebhookEndpoints.CountAsync(w => w.CompanyId == companyId);
        if (existing >= MaxEndpointsPerCompany)
            return ServiceResult<CreateWebhookResponse>.Fail(
                $"Maximum of {MaxEndpointsPerCompany} webhook endpoints per company reached.", 400);

        // Normalise event names to canonical enum casing; empty list = all events.
        var events = request.Events
            .Select(ev => Enum.Parse<WebhookEvent>(ev, true).ToString())
            .Distinct()
            .ToArray();

        var rawSecret = $"whsec_{SecurityHelper.GenerateSecureToken(32)}";

        var endpoint = new WebhookEndpoint
        {
            CompanyId = companyId,
            Url = url,
            Description = request.Description.Trim(),
            SecretHash = SecurityHelper.HashToken(rawSecret),
            SecretPrefix = rawSecret[..10],
            Events = events,
            IsActive = true
        };

        _db.WebhookEndpoints.Add(endpoint);
        await _db.SaveChangesAsync();

        return ServiceResult<CreateWebhookResponse>.Success(new CreateWebhookResponse
        {
            Id = endpoint.Id,
            Url = endpoint.Url,
            Description = endpoint.Description,
            SigningSecret = rawSecret,
            Events = endpoint.Events.ToList(),
            CreatedAt = endpoint.CreatedAt
        }, 201);
    }

    public async Task<ServiceResult<List<WebhookResponse>>> GetEndpointsAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<WebhookResponse>>.Fail("Access denied.", 403);

        var endpoints = await _db.WebhookEndpoints
            .Where(w => w.CompanyId == companyId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();

        return ServiceResult<List<WebhookResponse>>.Success(endpoints.Select(w => new WebhookResponse
        {
            Id = w.Id,
            Url = w.Url,
            Description = w.Description,
            SecretPrefix = w.SecretPrefix,
            Events = w.Events.ToList(),
            IsActive = w.IsActive,
            TotalDeliveries = w.TotalDeliveries,
            FailedDeliveries = w.FailedDeliveries,
            LastTriggeredAt = w.LastTriggeredAt,
            CreatedAt = w.CreatedAt
        }).ToList());
    }

    public async Task<ServiceResult> DeleteEndpointAsync(Guid endpointId, Guid userId)
    {
        var endpoint = await _db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == endpointId);
        if (endpoint == null) return ServiceResult.Fail("Webhook endpoint not found.", 404);

        bool isAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == endpoint.CompanyId && cu.UserId == userId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isAdmin) return ServiceResult.Fail("Only owners and admins can manage webhooks.", 403);

        _db.WebhookEndpoints.Remove(endpoint);
        await _db.SaveChangesAsync();

        return ServiceResult.Success(204);
    }

    public async Task<ServiceResult<List<WebhookDeliveryResponse>>> GetDeliveriesAsync(Guid endpointId, Guid userId)
    {
        var endpoint = await _db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == endpointId);
        if (endpoint == null) return ServiceResult<List<WebhookDeliveryResponse>>.Fail("Webhook endpoint not found.", 404);

        bool isMember = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == endpoint.CompanyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<WebhookDeliveryResponse>>.Fail("Access denied.", 403);

        var deliveries = await _db.WebhookDeliveries
            .Where(d => d.WebhookEndpointId == endpointId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(50)
            .Select(d => new WebhookDeliveryResponse
            {
                Id = d.Id,
                EventName = d.EventName,
                Status = d.Status.ToString(),
                AttemptCount = d.AttemptCount,
                ResponseStatusCode = d.ResponseStatusCode,
                ErrorMessage = d.ErrorMessage,
                DeliveredAt = d.DeliveredAt,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<WebhookDeliveryResponse>>.Success(deliveries);
    }

    public async Task<ServiceResult> RetryDeliveryAsync(Guid deliveryId, Guid userId)
    {
        var delivery = await _db.WebhookDeliveries
            .Include(d => d.WebhookEndpoint)
            .FirstOrDefaultAsync(d => d.Id == deliveryId);
        if (delivery == null) return ServiceResult.Fail("Delivery not found.", 404);

        bool isMember = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == delivery.WebhookEndpoint.CompanyId && cu.UserId == userId);
        if (!isMember) return ServiceResult.Fail("Access denied.", 403);

        if (delivery.Status == WebhookDeliveryStatus.Succeeded)
            return ServiceResult.Fail("Delivery already succeeded.", 400);
        if (!delivery.WebhookEndpoint.IsActive)
            return ServiceResult.Fail("Webhook endpoint is inactive.", 400);

        delivery.Status = WebhookDeliveryStatus.Retrying;
        delivery.NextRetryAt = DateTime.UtcNow;
        delivery.ErrorMessage = null;
        await _db.SaveChangesAsync();

        return ServiceResult.Success(202);
    }

    public async Task DispatchEventAsync(Guid companyId, WebhookEvent eventType, object payload)
    {
        var eventName = eventType.ToString();

        var endpoints = await _db.WebhookEndpoints
            .Where(w => w.CompanyId == companyId && w.IsActive)
            .ToListAsync();

        // Empty Events array means "subscribe to everything".
        var subscribed = endpoints
            .Where(w => w.Events.Length == 0 ||
                        w.Events.Contains(eventName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (subscribed.Count == 0) return;

        var body = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            @event = eventName,
            createdAt = DateTime.UtcNow,
            data = payload
        });

        foreach (var endpoint in subscribed)
        {
            _db.WebhookDeliveries.Add(new WebhookDelivery
            {
                WebhookEndpointId = endpoint.Id,
                EventName = eventName,
                Payload = body,
                Status = WebhookDeliveryStatus.Pending,
                NextRetryAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task ProcessPendingDeliveriesAsync()
    {
        var now = DateTime.UtcNow;

        var due = await _db.WebhookDeliveries
            .Include(d => d.WebhookEndpoint)
            .Where(d =>
                (d.Status == WebhookDeliveryStatus.Pending || d.Status == WebhookDeliveryStatus.Retrying) &&
                (d.NextRetryAt == null || d.NextRetryAt <= now))
            .OrderBy(d => d.CreatedAt)
            .Take(DeliveryBatchSize)
            .ToListAsync();

        foreach (var delivery in due)
        {
            if (!delivery.WebhookEndpoint.IsActive)
            {
                delivery.Status = WebhookDeliveryStatus.Failed;
                delivery.ErrorMessage = "Endpoint deactivated.";
                continue;
            }

            await SendAsync(delivery);
        }

        if (due.Count > 0)
            await _db.SaveChangesAsync();
    }

    private async Task SendAsync(WebhookDelivery delivery)
    {
        var endpoint = delivery.WebhookEndpoint;
        delivery.AttemptCount++;

        try
        {
            var client = _http.CreateClient("WebhookClient");
            client.Timeout = TimeSpan.FromSeconds(DeliveryTimeoutSeconds);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signature = ComputeSignature(endpoint.SecretHash, timestamp, delivery.Payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Webhook-Id", delivery.Id.ToString());
            request.Headers.Add("X-Webhook-Event", delivery.EventName);
            request.Headers.Add("X-Webhook-Signature", $"t={timestamp},v1={signature}");

            using var response = await client.SendAsync(request);

            delivery.ResponseStatusCode = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync();
            delivery.ResponseBody = responseBody.Length > 2000 ? responseBody[..2000] : responseBody;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = WebhookDeliveryStatus.Succeeded;
                delivery.DeliveredAt = DateTime.UtcNow;
                delivery.NextRetryAt = null;
                delivery.ErrorMessage = null;
                endpoint.TotalDeliveries++;
                endpoint.LastTriggeredAt = DateTime.UtcNow;
                return;
            }

            HandleFailure(delivery, $"Endpoint returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            HandleFailure(delivery, ex.Message);
        }
    }

    private void HandleFailure(WebhookDelivery delivery, string error)
    {
        delivery.ErrorMessage = error.Length > 500 ? error[..500] : error;

        if (delivery.AttemptCount >= MaxRetryAttempts)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.NextRetryAt = null;
            delivery.WebhookEndpoint.TotalDeliveries++;
            delivery.WebhookEndpoint.FailedDeliveries++;
            _logger.LogWarning("Webhook delivery {Id} permanently failed after {Attempts} attempts: {Error}",
                delivery.Id, delivery.AttemptCount, error);
            return;
        }

        // Exponential backoff: 5, 10, 20, 40 … minutes
        var delayMinutes = 5 * Math.Pow(2, delivery.AttemptCount - 1);
        delivery.Status = WebhookDeliveryStatus.Retrying;
        delivery.NextRetryAt = DateTime.UtcNow.AddMinutes(delayMinutes);
    }

    private static string ComputeSignature(string secretHash, long timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretHash));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
