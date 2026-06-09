using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class TwoFactorService : ITwoFactorService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly RefreshTokenService _refreshTokenService;
    private readonly IConfiguration _config;

    private int OtpExpiryMinutes => int.Parse(_config["Security:TwoFactor:OtpExpiryMinutes"] ?? "10");
    private int SessionExpiryMinutes => int.Parse(_config["Security:TwoFactor:SessionExpiryMinutes"] ?? "10");

    public TwoFactorService(
        ApplicationDbContext db,
        IEmailService emailService,
        RefreshTokenService refreshTokenService,
        IConfiguration config)
    {
        _db = db;
        _emailService = emailService;
        _refreshTokenService = refreshTokenService;
        _config = config;
    }

    public async Task<ServiceResult<TwoFactorStatusResponse>> GetStatusAsync(Guid userId)
    {
        var tf = await _db.UserTwoFactors.FirstOrDefaultAsync(x => x.UserId == userId);

        return ServiceResult<TwoFactorStatusResponse>.Success(new TwoFactorStatusResponse
        {
            IsEnabled = tf?.IsEnabled ?? false,
            Method = tf?.Method.ToString(),
            TotpConfirmed = tf?.TotpConfirmed ?? false
        });
    }

    // ── Email OTP ─────────────────────────────────────────────────────────────

    public async Task<ServiceResult<TwoFactorChallengeResponse>> SendEmailOtpAsync(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return ServiceResult<TwoFactorChallengeResponse>.Fail("User not found.", 404);

        // Create / update UserTwoFactor record
        var tf = await _db.UserTwoFactors.FirstOrDefaultAsync(x => x.UserId == userId)
                 ?? new UserTwoFactor { UserId = userId };

        var otp = SecurityHelper.GenerateNumericOtp();
        tf.Method = TwoFactorMethod.EmailOtp;
        tf.EmailOtpCodeHash = BCrypt.Net.BCrypt.HashPassword(otp);
        tf.EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(OtpExpiryMinutes);
        tf.UpdatedAt = DateTime.UtcNow;

        if (tf.Id == Guid.Empty)
        {
            tf.Id = Guid.NewGuid();
            _db.UserTwoFactors.Add(tf);
        }

        // Create a pending 2FA session token
        var sessionToken = SecurityHelper.GenerateSecureToken(32);
        _db.TwoFactorSessions.Add(new TwoFactorSession
        {
            UserId = userId,
            SessionToken = sessionToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(SessionExpiryMinutes)
        });

        await _db.SaveChangesAsync();

        // Send OTP email
        await _emailService.SendAsync(
            to: user.Email,
            subject: "Your OcrInvoiceSaaS login code",
            body: $"Your one-time login code is: <strong>{otp}</strong><br/>It expires in {OtpExpiryMinutes} minutes.");

        return ServiceResult<TwoFactorChallengeResponse>.Success(new TwoFactorChallengeResponse
        {
            TwoFactorSessionToken = sessionToken,
            Method = "EmailOtp",
            MaskedEmail = SecurityHelper.MaskEmail(user.Email),
            ExpiresAt = DateTime.UtcNow.AddMinutes(SessionExpiryMinutes)
        });
    }

    public async Task<ServiceResult<TwoFactorChallengeResponse>> ResendOtpAsync(ResendOtpRequest request)
    {
        var session = await _db.TwoFactorSessions
            .FirstOrDefaultAsync(s => s.SessionToken == request.TwoFactorSessionToken && !s.IsUsed);

        if (session == null || DateTime.UtcNow > session.ExpiresAt)
            return ServiceResult<TwoFactorChallengeResponse>.Fail("Session expired. Please log in again.", 401);

        // Invalidate old session and issue a new one with a fresh OTP
        session.IsUsed = true;
        await _db.SaveChangesAsync();

        return await SendEmailOtpAsync(session.UserId);
    }

    // ── TOTP Setup ────────────────────────────────────────────────────────────

    public async Task<ServiceResult<TotpSetupResponse>> SetupTotpAsync(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return ServiceResult<TotpSetupResponse>.Fail("User not found.", 404);

        var tf = await _db.UserTwoFactors.FirstOrDefaultAsync(x => x.UserId == userId)
                 ?? new UserTwoFactor { Id = Guid.NewGuid(), UserId = userId };

        // Generate a new secret (shown once — user must save it)
        var secret = SecurityHelper.GenerateTotpSecret();
        tf.TotpSecretKey = secret;
        tf.Method = TwoFactorMethod.Totp;
        tf.TotpConfirmed = false; // requires confirmation step before enabling
        tf.UpdatedAt = DateTime.UtcNow;

        if (!await _db.UserTwoFactors.AnyAsync(x => x.UserId == userId))
            _db.UserTwoFactors.Add(tf);

        await _db.SaveChangesAsync();

        return ServiceResult<TotpSetupResponse>.Success(new TotpSetupResponse
        {
            SecretKey = secret,
            QrCodeUri = SecurityHelper.BuildTotpUri(secret, user.Email),
            ManualEntryKey = string.Join(" ", Enumerable.Range(0, secret.Length / 4)
                .Select(i => secret.Substring(i * 4, 4))) // "JBSW Y3DP ..." for readability
        });
    }

    public async Task<ServiceResult> ConfirmTotpSetupAsync(Guid userId, ConfirmTotpRequest request)
    {
        var tf = await _db.UserTwoFactors.FirstOrDefaultAsync(x => x.UserId == userId);
        if (tf?.TotpSecretKey == null)
            return ServiceResult.Fail("TOTP setup not started. Call /2fa/totp/setup first.", 400);

        if (!SecurityHelper.ValidateTotpCode(tf.TotpSecretKey, request.Code))
            return ServiceResult.Fail("Invalid TOTP code.", 400);

        tf.TotpConfirmed = true;
        tf.IsEnabled = true;
        tf.UpdatedAt = DateTime.UtcNow;

        var user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            user.TwoFactorEnabled = true;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    // ── Shared Verification ───────────────────────────────────────────────────

    public async Task<ServiceResult<TokenResponse>> VerifyAndIssueTokensAsync(
        VerifyTwoFactorRequest request, string? ipAddress, string? deviceInfo)
    {
        var session = await _db.TwoFactorSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.SessionToken == request.TwoFactorSessionToken && !s.IsUsed);

        if (session == null || DateTime.UtcNow > session.ExpiresAt)
            return ServiceResult<TokenResponse>.Fail("2FA session expired. Please log in again.", 401);

        var tf = await _db.UserTwoFactors.FirstOrDefaultAsync(x => x.UserId == session.UserId);
        if (tf == null)
            return ServiceResult<TokenResponse>.Fail("2FA not configured.", 400);

        bool codeValid = tf.Method switch
        {
            TwoFactorMethod.EmailOtp => VerifyEmailOtp(tf, request.Code),
            TwoFactorMethod.Totp => SecurityHelper.ValidateTotpCode(tf.TotpSecretKey!, request.Code),
            _ => false
        };

        if (!codeValid)
            return ServiceResult<TokenResponse>.Fail("Invalid or expired code.", 401);

        // Mark session as used
        session.IsUsed = true;

        // Clear OTP fields after use
        if (tf.Method == TwoFactorMethod.EmailOtp)
        {
            tf.EmailOtpCodeHash = null;
            tf.EmailOtpExpiresAt = null;
        }

        await _db.SaveChangesAsync();

        // Issue full token pair
        var (access, accessExpiry, refresh, refreshExpiry) =
            await _refreshTokenService.IssueTokensAsync(session.User, ipAddress, deviceInfo);

        return ServiceResult<TokenResponse>.Success(new TokenResponse
        {
            AccessToken = access,
            RefreshToken = refresh,
            AccessTokenExpiresAt = accessExpiry,
            RefreshTokenExpiresAt = refreshExpiry,
            FullName = session.User.FullName,
            Email = session.User.Email,
            UserId = session.User.Id
        });
    }

    public async Task<ServiceResult> DisableTwoFactorAsync(Guid userId, string currentPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return ServiceResult.Fail("User not found.", 404);

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return ServiceResult.Fail("Incorrect password.", 401);

        var tf = await _db.UserTwoFactors.FirstOrDefaultAsync(x => x.UserId == userId);
        if (tf != null)
        {
            tf.IsEnabled = false;
            tf.TotpSecretKey = null;
            tf.TotpConfirmed = false;
            tf.EmailOtpCodeHash = null;
            tf.EmailOtpExpiresAt = null;
            tf.UpdatedAt = DateTime.UtcNow;
        }

        user.TwoFactorEnabled = false;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static bool VerifyEmailOtp(UserTwoFactor tf, string code)
    {
        if (tf.EmailOtpCodeHash == null || tf.EmailOtpExpiresAt == null) return false;
        if (DateTime.UtcNow > tf.EmailOtpExpiresAt) return false;
        return BCrypt.Net.BCrypt.Verify(code, tf.EmailOtpCodeHash);
    }
}
