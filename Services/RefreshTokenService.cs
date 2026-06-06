using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly JwtHelper _jwtHelper;

    private int RefreshTokenDays => int.Parse(_config["Jwt:RefreshTokenDays"] ?? "30");

    public RefreshTokenService(ApplicationDbContext db, IConfiguration config, JwtHelper jwtHelper)
    {
        _db = db;
        _config = config;
        _jwtHelper = jwtHelper;
    }

    /// <summary>
    /// Issues a new access + refresh token pair.
    /// Called from AuthService after a successful login or 2FA verification.
    /// </summary>
    public async Task<(string accessToken, DateTime accessExpiry, string rawRefreshToken, DateTime refreshExpiry)>
        IssueTokensAsync(User user, string? ipAddress, string? deviceInfo)
    {
        var (accessToken, accessExpiry) = _jwtHelper.GenerateToken(user.Id, user.Email, user.FullName);

        // Generate cryptographically random refresh token
        var rawToken = SecurityHelper.GenerateSecureToken(64);
        var tokenHash = SecurityHelper.HashToken(rawToken);
        var refreshExpiry = DateTime.UtcNow.AddDays(RefreshTokenDays);

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = string.Empty,   // never stored plain
            TokenHash = tokenHash,
            DeviceInfo = deviceInfo?[..Math.Min(deviceInfo.Length, 500)],
            IpAddress = ipAddress,
            ExpiresAt = refreshExpiry
        });

        await _db.SaveChangesAsync();

        return (accessToken, accessExpiry, rawToken, refreshExpiry);
    }

    public async Task<ServiceResult<TokenResponse>> RefreshAsync(
        RefreshRequest request, string? ipAddress, string? deviceInfo)
    {
        // Validate the expired (but structurally valid) access token to extract user ID
        var principal = GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
            return ServiceResult<TokenResponse>.Fail("Invalid access token.", 401);

        if (!Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return ServiceResult<TokenResponse>.Fail("Invalid token claims.", 401);

        var tokenHash = SecurityHelper.HashToken(request.RefreshToken);

        var storedToken = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.UserId == userId);

        if (storedToken == null)
            return ServiceResult<TokenResponse>.Fail("Refresh token not found.", 401);

        if (!storedToken.IsActive)
        {
            // Detect reuse of already-used token — revoke entire chain
            if (storedToken.IsUsed)
            {
                await RevokeAllAsync(userId);
                return ServiceResult<TokenResponse>.Fail(
                    "Refresh token reuse detected. All sessions revoked for security.", 401);
            }

            return ServiceResult<TokenResponse>.Fail("Refresh token is expired or revoked.", 401);
        }

        // Mark old token as used
        storedToken.IsUsed = true;
        storedToken.RevokedAt = DateTime.UtcNow;

        // Issue new pair
        var (newAccess, newAccessExpiry, newRefresh, newRefreshExpiry) =
            await IssueTokensAsync(storedToken.User, ipAddress, deviceInfo);

        // Track the chain
        storedToken.ReplacedByTokenId = SecurityHelper.HashToken(newRefresh)[..20];

        await _db.SaveChangesAsync();

        return ServiceResult<TokenResponse>.Success(new TokenResponse
        {
            AccessToken = newAccess,
            RefreshToken = newRefresh,
            AccessTokenExpiresAt = newAccessExpiry,
            RefreshTokenExpiresAt = newRefreshExpiry,
            FullName = storedToken.User.FullName,
            Email = storedToken.User.Email,
            UserId = storedToken.User.Id
        });
    }

    public async Task<ServiceResult> RevokeAsync(string rawToken, Guid userId)
    {
        var tokenHash = SecurityHelper.HashToken(rawToken);
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && rt.UserId == userId);

        if (stored == null || stored.IsRevoked)
            return ServiceResult.Fail("Token not found or already revoked.", 404);

        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RevokeAllAsync(Guid userId)
    {
        await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked && !rt.IsUsed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rt => rt.IsRevoked, true)
                .SetProperty(rt => rt.RevokedAt, DateTime.UtcNow));

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<List<ActiveSessionResponse>>> GetActiveSessionsAsync(
        Guid userId, string? currentRawToken)
    {
        var currentHash = currentRawToken != null ? SecurityHelper.HashToken(currentRawToken) : null;

        var sessions = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.IsActive)
            .OrderByDescending(rt => rt.CreatedAt)
            .Select(rt => new ActiveSessionResponse
            {
                Id = rt.Id,
                DeviceInfo = rt.DeviceInfo,
                IpAddress = rt.IpAddress,
                CreatedAt = rt.CreatedAt,
                ExpiresAt = rt.ExpiresAt,
                IsCurrent = rt.TokenHash == currentHash
            })
            .ToListAsync();

        return ServiceResult<List<ActiveSessionResponse>>.Success(sessions);
    }

    public async Task PurgeExpiredAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7); // keep 7 days of history for audit
        await _db.RefreshTokens
            .Where(rt => (rt.IsRevoked || rt.IsUsed || rt.ExpiresAt < DateTime.UtcNow)
                         && rt.CreatedAt < cutoff)
            .ExecuteDeleteAsync();
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var tokenValidationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = false, // we want to accept expired tokens here
            ValidateIssuerSigningKey = true,
            ValidIssuer = _config["Jwt:Issuer"],
            ValidAudience = _config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!))
        };

        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, tokenValidationParams, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt ||
                !jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
                return null;

            return principal;
        }
        catch
        {
            return null;
        }
    }
}
