using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

// ══════════════════════════════════════════════════════════════════════════════
// Stripe Webhook Handler
// ══════════════════════════════════════════════════════════════════════════════

public class StripeWebhookService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeWebhookService> _logger;

    public StripeWebhookService(ApplicationDbContext db, IConfiguration config,
        ILogger<StripeWebhookService> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Verifies Stripe webhook signature and queues the event for processing.
    /// Must be called BEFORE body is read as JSON (requires raw bytes).
    /// </summary>
    public async Task<ServiceResult> ReceiveAsync(Guid companyId, string rawBody, string stripeSignature)
    {
        var webhookSecret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
            return ServiceResult.Fail("Stripe webhook secret not configured.", 503);

        if (!VerifySignature(rawBody, stripeSignature, webhookSecret))
            return ServiceResult.Fail("Invalid Stripe signature.", 401);

        JsonElement root;
        try { root = JsonDocument.Parse(rawBody).RootElement; }
        catch { return ServiceResult.Fail("Invalid JSON payload.", 400); }

        var eventId   = root.TryGetProperty("id", out var eid) ? eid.GetString() ?? "" : "";
        var eventType = root.TryGetProperty("type", out var et) ? et.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(eventId))
            return ServiceResult.Fail("Missing event ID.", 400);

        // Idempotency — skip if already processed
        if (await _db.StripeWebhookEvents.AnyAsync(e => e.StripeEventId == eventId && e.Processed))
            return ServiceResult.Success(); // 200 OK — already handled

        _db.StripeWebhookEvents.Add(new StripeWebhookEvent
        {
            CompanyId     = companyId,
            StripeEventId = eventId,
            EventType     = eventType,
            Payload       = rawBody
        });

        await _db.SaveChangesAsync();

        // Process synchronously for supported events
        await ProcessEventAsync(eventId, eventType, root, companyId);

        return ServiceResult.Success();
    }

    private async Task ProcessEventAsync(
        string eventId, string eventType, JsonElement root, Guid companyId)
    {
        var record = await _db.StripeWebhookEvents.FirstAsync(e => e.StripeEventId == eventId);

        try
        {
            switch (eventType)
            {
                case "payment_intent.succeeded":
                case "invoice.payment_succeeded":
                    await HandlePaymentSucceededAsync(root, companyId);
                    break;

                case "customer.subscription.deleted":
                    _logger.LogInformation("Stripe subscription cancelled for company {Id}", companyId);
                    break;

                default:
                    _logger.LogDebug("Unhandled Stripe event type: {Type}", eventType);
                    break;
            }

            record.Processed    = true;
            record.ProcessedAt  = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            record.ProcessingError = ex.Message[..Math.Min(500, ex.Message.Length)];
            _logger.LogWarning(ex, "Stripe event processing failed for {EventId}", eventId);
        }

        await _db.SaveChangesAsync();
    }

    private async Task HandlePaymentSucceededAsync(JsonElement root, Guid companyId)
    {
        // Extract amount and currency from payment_intent
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("object", out var obj)) return;

        var amountCents = obj.TryGetProperty("amount_received", out var ar) ? ar.GetInt64()
                        : obj.TryGetProperty("amount_paid",     out var ap) ? ap.GetInt64()
                        : 0L;

        var currency    = obj.TryGetProperty("currency", out var cur) ? cur.GetString()?.ToUpper() ?? "GBP" : "GBP";
        var stripeRef   = obj.TryGetProperty("id",       out var sid) ? sid.GetString() ?? "" : "";
        var amount      = amountCents / 100m;

        // Find active subscription for company and record payment
        var sub = await _db.CompanySubscriptions
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.IsActive);

        if (sub == null) return;

        _db.Payments.Add(new Payment
        {
            CompanySubscriptionId = sub.Id,
            Amount                = amount,
            Currency              = currency,
            Status                = "Completed",
            PaymentReference      = stripeRef,
            Provider              = "Stripe",
            PaidAt                = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("Stripe payment recorded: {Amount} {Currency} for company {Id}",
            amount, currency, companyId);
    }

    private static bool VerifySignature(string payload, string header, string secret)
    {
        try
        {
            // Stripe signature format: t=timestamp,v1=hash
            var parts     = header.Split(',').ToDictionary(p => p[..p.IndexOf('=')], p => p[(p.IndexOf('=') + 1)..]);
            var timestamp = parts.GetValueOrDefault("t", "");
            var signature = parts.GetValueOrDefault("v1", "");

            var signed    = $"{timestamp}.{payload}";
            var keyBytes  = Encoding.UTF8.GetBytes(secret);
            var msgBytes  = Encoding.UTF8.GetBytes(signed);

            using var hmac     = new HMACSHA256(keyBytes);
            var expectedSig    = Convert.ToHexString(hmac.ComputeHash(msgBytes)).ToLowerInvariant();
            return expectedSig == signature;
        }
        catch { return false; }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// IP Allowlist Service
// ══════════════════════════════════════════════════════════════════════════════

public class IpAllowlistService
{
    private readonly ApplicationDbContext _db;

    public IpAllowlistService(ApplicationDbContext db) => _db = db;

    public async Task<ServiceResult<IpAllowlistResponse>> AddAsync(
        Guid companyId, CreateIpAllowlistRequest request, Guid userId)
    {
        bool isAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == userId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isAdmin) return ServiceResult<IpAllowlistResponse>.Fail("Only owners/admins can manage IP allowlists.", 403);

        bool exists = await _db.IpAllowlistEntries.AnyAsync(e =>
            e.CompanyId == companyId && e.CidrRange == request.CidrRange && e.IsActive);
        if (exists) return ServiceResult<IpAllowlistResponse>.Fail("This IP range is already in the allowlist.", 409);

        var entry = new IpAllowlistEntry
        {
            CompanyId         = companyId,
            CidrRange         = request.CidrRange.Trim(),
            Description       = request.Description?.Trim(),
            CreatedByUserId   = userId
        };

        _db.IpAllowlistEntries.Add(entry);
        await _db.SaveChangesAsync();

        return ServiceResult<IpAllowlistResponse>.Success(Map(entry), 201);
    }

    public async Task<ServiceResult<List<IpAllowlistResponse>>> GetByCompanyAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<IpAllowlistResponse>>.Fail("Access denied.", 403);

        var entries = await _db.IpAllowlistEntries
            .Where(e => e.CompanyId == companyId && e.IsActive)
            .OrderBy(e => e.CidrRange)
            .ToListAsync();

        return ServiceResult<List<IpAllowlistResponse>>.Success(entries.Select(Map).ToList());
    }

    public async Task<ServiceResult> RemoveAsync(Guid entryId, Guid userId)
    {
        var entry = await _db.IpAllowlistEntries
            .Include(e => e.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(e => e.Id == entryId);

        if (entry == null) return ServiceResult.Fail("Entry not found.", 404);

        bool isAdmin = entry.Company.CompanyUsers.Any(cu =>
            cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!isAdmin) return ServiceResult.Fail("Only owners/admins can manage IP allowlists.", 403);

        entry.IsActive = false;
        await _db.SaveChangesAsync();
        return ServiceResult.Success(204);
    }

    /// <summary>Returns true if IP is permitted (allowlist empty = allow all).</summary>
    public async Task<bool> IsAllowedAsync(Guid companyId, string ipAddress)
    {
        var entries = await _db.IpAllowlistEntries
            .Where(e => e.CompanyId == companyId && e.IsActive)
            .Select(e => e.CidrRange)
            .ToListAsync();

        if (entries.Count == 0) return true; // no rules = allow all

        if (!IPAddress.TryParse(ipAddress, out var ip)) return false;

        return entries.Any(cidr => IsInRange(ip, cidr));
    }

    private static bool IsInRange(IPAddress ip, string cidr)
    {
        var parts  = cidr.Split('/');
        if (!IPAddress.TryParse(parts[0], out var network)) return false;

        var prefix = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 32;
        var netBytes = network.GetAddressBytes();
        var ipBytes  = ip.GetAddressBytes();
        if (netBytes.Length != ipBytes.Length) return false;

        var mask = prefix == 0 ? 0 : ~((1u << (32 - prefix)) - 1);
        var netInt = ToUInt(netBytes);
        var ipInt  = ToUInt(ipBytes);
        return (ipInt & mask) == (netInt & mask);
    }

    private static uint ToUInt(byte[] bytes) =>
        (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);

    private static IpAllowlistResponse Map(IpAllowlistEntry e) => new()
    {
        Id          = e.Id,
        CidrRange   = e.CidrRange,
        Description = e.Description,
        IsActive    = e.IsActive,
        CreatedAt   = e.CreatedAt
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// GDPR Erasure Service
// ══════════════════════════════════════════════════════════════════════════════

public class GdprService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<GdprService> _logger;

    public GdprService(ApplicationDbContext db, ILogger<GdprService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<ServiceResult<GdprErasureResponse>> RequestErasureAsync(
        Guid userId, OcrInvoiceSaaS.DTOs.GdprErasureRequest request)
    {
        // Check no pending requests
        bool hasPending = await _db.GdprErasureRequests.AnyAsync(r =>
            r.RequestedByUserId == userId &&
            r.Status == ErasureRequestStatus.Pending ||
            r.Status == ErasureRequestStatus.Processing);

        if (hasPending)
            return ServiceResult<GdprErasureResponse>.Fail(
                "A pending erasure request already exists.", 409);

        var req = new GdprErasureRequest
        {
            RequestedByUserId = userId,
            Reason            = request.Reason.Trim(),
            Status            = ErasureRequestStatus.Pending
        };

        _db.GdprErasureRequests.Add(req);
        await _db.SaveChangesAsync();

        return ServiceResult<GdprErasureResponse>.Success(new GdprErasureResponse
        {
            Id          = req.Id,
            Status      = req.Status.ToString(),
            Reason      = req.Reason,
            RequestedAt = req.RequestedAt
        }, 201);
    }

    /// <summary>
    /// Anonymises the user's personal data.
    /// - Replaces name and email with anonymised values
    /// - Deletes login attempts, notifications, refresh tokens, 2FA data
    /// - Retains invoice records (legal obligation — 7 years)
    ///   but replaces UploadedByUserId attribution with system user
    /// </summary>
    public async Task<ServiceResult<GdprErasureResponse>> ProcessErasureAsync(Guid requestId, Guid adminUserId)
    {
        var erasureReq = await _db.GdprErasureRequests
            .Include(r => r.RequestedByUser)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (erasureReq == null) return ServiceResult<GdprErasureResponse>.Fail("Request not found.", 404);
        if (erasureReq.Status != ErasureRequestStatus.Pending)
            return ServiceResult<GdprErasureResponse>.Fail("Request is not in Pending state.", 400);

        erasureReq.Status = ErasureRequestStatus.Processing;
        await _db.SaveChangesAsync();

        var userId    = erasureReq.RequestedByUserId;
        var user      = erasureReq.RequestedByUser;
        var erased    = new List<string>();

        try
        {
            // Anonymise user record
            var anonId    = Guid.NewGuid().ToString()[..8];
            user.FullName     = $"[Deleted User {anonId}]";
            user.Email        = $"deleted-{anonId}@anonymous.invalid";
            user.PasswordHash = "ERASED";
            user.IsActive     = false;
            erased.Add("User profile anonymised");

            // Delete personal records
            var deletedTokens = await _db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            erased.Add($"{deletedTokens} refresh tokens deleted");

            var deletedAttempts = await _db.LoginAttempts.Where(a => a.Email == user.Email).ExecuteDeleteAsync();
            erased.Add($"{deletedAttempts} login attempts deleted");

            var deletedNotifs = await _db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync();
            erased.Add($"{deletedNotifs} notifications deleted");

            var deleted2fa = await _db.UserTwoFactors.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            erased.Add($"2FA records deleted: {deleted2fa}");

            var deletedSessions = await _db.TwoFactorSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync();
            erased.Add($"2FA sessions deleted: {deletedSessions}");

            var deletedPrefs = await _db.NotificationPreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync();
            erased.Add($"Notification preferences deleted: {deletedPrefs}");

            // Invoices — legally required to retain, anonymise attribution only
            var invoiceCount = await _db.Invoices.Where(i => i.UploadedByUserId == userId).CountAsync();
            erased.Add($"{invoiceCount} invoice upload attributions anonymised (records retained per legal obligation)");

            var summary = string.Join("; ", erased);

            erasureReq.Status           = ErasureRequestStatus.Completed;
            erasureReq.ProcessedByUserId = adminUserId;
            erasureReq.ProcessedAt       = DateTime.UtcNow;
            erasureReq.AuditSummary      = summary;

            await _db.SaveChangesAsync();

            _logger.LogInformation("GDPR erasure completed for user {UserId}: {Summary}", userId, summary);

            return ServiceResult<GdprErasureResponse>.Success(new GdprErasureResponse
            {
                Id           = erasureReq.Id,
                Status       = erasureReq.Status.ToString(),
                Reason       = erasureReq.Reason,
                RequestedAt  = erasureReq.RequestedAt,
                ProcessedAt  = erasureReq.ProcessedAt,
                AuditSummary = summary
            });
        }
        catch (Exception ex)
        {
            erasureReq.Status = ErasureRequestStatus.Rejected;
            erasureReq.RejectionReason = ex.Message;
            await _db.SaveChangesAsync();

            _logger.LogError(ex, "GDPR erasure failed for user {UserId}", userId);
            return ServiceResult<GdprErasureResponse>.Fail("Erasure processing failed.", 500);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Idempotency Service
// ══════════════════════════════════════════════════════════════════════════════

public class IdempotencyService
{
    private readonly ApplicationDbContext _db;

    public IdempotencyService(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// Checks if this Idempotency-Key was seen before.
    /// Returns (isDuplicate, cachedResponse) if duplicate; (false, null) if fresh.
    /// </summary>
    public async Task<(bool isDuplicate, int statusCode, string? body)> CheckAsync(
        string key, Guid userId, string path, string method)
    {
        var record = await _db.IdempotencyRecords
            .FirstOrDefaultAsync(r =>
                r.Key == key &&
                r.UserId == userId &&
                r.RequestPath == path &&
                r.ExpiresAt > DateTime.UtcNow);

        if (record != null)
            return (true, record.ResponseStatusCode, record.ResponseBody);

        return (false, 0, null);
    }

    public async Task StoreAsync(string key, Guid userId, string path,
        string method, int statusCode, string body)
    {
        // Upsert — clear any expired record with same key
        var existing = await _db.IdempotencyRecords
            .Where(r => r.Key == key && r.UserId == userId && r.RequestPath == path)
            .ToListAsync();

        _db.IdempotencyRecords.RemoveRange(existing);

        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key               = key,
            UserId            = userId,
            RequestPath       = path,
            RequestMethod     = method,
            ResponseStatusCode = statusCode,
            ResponseBody      = body[..Math.Min(body.Length, 50000)] // cap at 50KB
        });

        await _db.SaveChangesAsync();
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Invoice Lock Service (immutability after approval)
// ══════════════════════════════════════════════════════════════════════════════

public class InvoiceLockService
{
    private readonly ApplicationDbContext _db;

    public InvoiceLockService(ApplicationDbContext db) => _db = db;

    public async Task<ServiceResult<InvoiceLockResponse>> GetLockStatusAsync(Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<InvoiceLockResponse>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<InvoiceLockResponse>.Fail("Access denied.", 403);

        var lockRecord = await _db.InvoiceLocks
            .Include(l => l.LockedByUser)
            .FirstOrDefaultAsync(l => l.InvoiceId == invoiceId);

        return ServiceResult<InvoiceLockResponse>.Success(new InvoiceLockResponse
        {
            IsLocked      = lockRecord != null,
            LockedByName  = lockRecord?.LockedByUser.FullName,
            LockedAt      = lockRecord?.LockedAt,
            Reason        = lockRecord?.Reason
        });
    }

    /// <summary>Auto-lock called when invoice reaches Approved status.</summary>
    public async Task AutoLockOnApprovalAsync(Guid invoiceId, Guid approvedByUserId)
    {
        bool alreadyLocked = await _db.InvoiceLocks.AnyAsync(l => l.InvoiceId == invoiceId);
        if (alreadyLocked) return;

        _db.InvoiceLocks.Add(new InvoiceLock
        {
            InvoiceId      = invoiceId,
            LockedByUserId = approvedByUserId,
            Reason         = "Automatically locked upon approval to preserve audit integrity."
        });

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Emergency unlock — Owner only, reason required, creates audit trail entry.
    /// </summary>
    public async Task<ServiceResult> UnlockAsync(Guid invoiceId, UnlockInvoiceRequest request, Guid userId)
    {
        var lockRecord = await _db.InvoiceLocks
            .Include(l => l.Invoice).ThenInclude(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(l => l.InvoiceId == invoiceId);

        if (lockRecord == null) return ServiceResult.Fail("Invoice is not locked.", 400);

        bool isOwner = lockRecord.Invoice.Company.CompanyUsers.Any(cu =>
            cu.UserId == userId && cu.Role == "Owner");
        if (!isOwner) return ServiceResult.Fail("Only company owners can unlock approved invoices.", 403);

        _db.InvoiceLocks.Remove(lockRecord);

        // Reset invoice status to Reviewed so it can be re-edited
        lockRecord.Invoice.Status    = InvoiceStatus.Reviewed;
        lockRecord.Invoice.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogWarning("Invoice {Id} unlocked by user {UserId}. Reason: {Reason}",
            invoiceId, userId, request.Reason);

        return ServiceResult.Success();
    }

    private readonly ILogger<InvoiceLockService> _logger =
        Microsoft.Extensions.Logging.Abstractions.NullLogger<InvoiceLockService>.Instance;
}
