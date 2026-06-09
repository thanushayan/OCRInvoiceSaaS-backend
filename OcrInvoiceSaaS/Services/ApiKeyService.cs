using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class ApiKeyService : IApiKeyService
{
    private readonly ApplicationDbContext _db;

    public ApiKeyService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceResult<CreateApiKeyResponse>> CreateAsync(
        Guid userId, Guid companyId, CreateApiKeyRequest request)
    {
        // Verify user belongs to the company
        bool isMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);

        if (!isMember)
            return ServiceResult<CreateApiKeyResponse>.Fail("Access denied.", 403);

        // Validate scopes against allowed set
        var invalidScopes = request.Scopes
            .Where(s => !AllowedScopes.Contains(s))
            .ToList();

        if (invalidScopes.Count > 0)
            return ServiceResult<CreateApiKeyResponse>.Fail(
                $"Invalid scopes: {string.Join(", ", invalidScopes)}. Allowed: {string.Join(", ", AllowedScopes)}", 400);

        var (rawKey, prefix, hash) = SecurityHelper.GenerateApiKey();

        var apiKey = new ApiKey
        {
            UserId = userId,
            CompanyId = companyId,
            Name = request.Name.Trim(),
            KeyPrefix = prefix,
            KeyHash = hash,
            Scopes = request.Scopes.Distinct().ToArray(),
            ExpiresAt = request.ExpiresAt,
            IsActive = true
        };

        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync();

        return ServiceResult<CreateApiKeyResponse>.Success(new CreateApiKeyResponse
        {
            Id = apiKey.Id,
            Name = apiKey.Name,
            RawKey = rawKey,           // shown exactly once
            KeyPrefix = prefix,
            Scopes = apiKey.Scopes.ToList(),
            ExpiresAt = apiKey.ExpiresAt,
            CreatedAt = apiKey.CreatedAt
        }, 201);
    }

    public async Task<ServiceResult<List<ApiKeyResponse>>> GetByCompanyAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);

        if (!isMember)
            return ServiceResult<List<ApiKeyResponse>>.Fail("Access denied.", 403);

        var keys = await _db.ApiKeys
            .Where(k => k.CompanyId == companyId && k.IsActive)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyResponse
            {
                Id = k.Id,
                Name = k.Name,
                KeyPrefix = k.KeyPrefix,
                Scopes = k.Scopes.ToList(),
                IsActive = k.IsActive,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt,
                LastUsedIp = k.LastUsedIp,
                CreatedAt = k.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<ApiKeyResponse>>.Success(keys);
    }

    public async Task<ServiceResult> RevokeAsync(Guid keyId, Guid userId)
    {
        var key = await _db.ApiKeys.FindAsync(keyId);

        if (key == null || !key.IsActive)
            return ServiceResult.Fail("API key not found.", 404);

        // Check the requesting user belongs to the key's company
        bool isMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == key.CompanyId && cu.UserId == userId);

        if (!isMember)
            return ServiceResult.Fail("Access denied.", 403);

        key.IsActive = false;
        key.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult.Success(204);
    }

    public async Task<(bool valid, Guid userId, Guid companyId, string[] scopes)> ValidateAsync(string rawKey)
    {
        var empty = (false, Guid.Empty, Guid.Empty, Array.Empty<string>());

        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith("ocri_"))
            return empty;

        var hash = SecurityHelper.HashToken(rawKey);

        var key = await _db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive);

        if (key == null || !key.IsValid)
            return empty;

        // Update last-used metadata asynchronously (fire and forget style via SaveChanges)
        key.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (true, key.UserId, key.CompanyId, key.Scopes);
    }

    // ── Allowed scopes catalogue ──────────────────────────────────────────────

    public static readonly HashSet<string> AllowedScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "invoices:read",
        "invoices:write",
        "invoices:delete",
        "vendors:read",
        "vendors:write",
        "dashboard:read",
        "ocr:run",
        "company:read"
    };
}
