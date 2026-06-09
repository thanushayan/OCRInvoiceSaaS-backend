using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ITwoFactorService _twoFactorService;
    private readonly IPasswordResetService _passwordResetService;
    private readonly CurrentUserProvider _currentUser;

    public AuthController(
        IAuthService authService,
        IRefreshTokenService refreshTokenService,
        ITwoFactorService twoFactorService,
        IPasswordResetService passwordResetService,
        CurrentUserProvider currentUser)
    {
        _authService = authService;
        _refreshTokenService = refreshTokenService;
        _twoFactorService = twoFactorService;
        _passwordResetService = passwordResetService;
        _currentUser = currentUser;
    }

    private string ClientIp =>
        Request.Headers["X-Forwarded-For"].FirstOrDefault()
        ?? HttpContext.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    private string DeviceInfo => Request.Headers.UserAgent.ToString();

    // ═══════════════════════════════════════════════════════════════════════════
    // Core
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Register a new user account.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Login with email + password.
    /// - If 2FA is off  → returns access + refresh tokens immediately.
    /// - If 2FA is on   → returns { requiresTwoFactor: true, twoFactorSessionToken }.
    ///   Client must call POST /2fa/verify to obtain tokens.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await ((AuthService)_authService).LoginAsync(request, ClientIp, DeviceInfo);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Get the currently authenticated user's profile.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var result = await _authService.GetCurrentUserAsync(_currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Feature 1 — Refresh Tokens
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exchange an expired access token + valid refresh token for a fresh pair.
    /// Tokens rotate on every call — reusing an old refresh token revokes all sessions.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await _refreshTokenService.RefreshAsync(request, ClientIp, DeviceInfo);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Revoke a single refresh token — logs out one device.</summary>
    [Authorize]
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RevokeTokenRequest request)
    {
        var result = await _refreshTokenService.RevokeAsync(request.RefreshToken, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Revoke all refresh tokens — logs out every device at once.</summary>
    [Authorize]
    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAll()
    {
        await _refreshTokenService.RevokeAllAsync(_currentUser.GetUserId());
        return NoContent();
    }

    /// <summary>
    /// List all active sessions for the current user.
    /// Pass the current refresh token via ?currentToken= to mark the active session.
    /// </summary>
    [Authorize]
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] string? currentToken = null)
    {
        var result = await _refreshTokenService.GetActiveSessionsAsync(_currentUser.GetUserId(), currentToken);
        return Ok(result.Data);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Feature 2 — Two-Factor Authentication
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Get the 2FA configuration status for the current user.</summary>
    [Authorize]
    [HttpGet("2fa/status")]
    public async Task<IActionResult> TwoFactorStatus()
    {
        var result = await _twoFactorService.GetStatusAsync(_currentUser.GetUserId());
        return Ok(result.Data);
    }

    /// <summary>
    /// Step 2 of login when requiresTwoFactor is true.
    /// Submit the session token from the login response + the 6-digit code.
    /// Returns full access + refresh tokens on success.
    /// </summary>
    [HttpPost("2fa/verify")]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] VerifyTwoFactorRequest request)
    {
        var result = await _twoFactorService.VerifyAndIssueTokensAsync(request, ClientIp, DeviceInfo);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Resend the Email OTP during an active 2FA session (rate-limited by session expiry).</summary>
    [HttpPost("2fa/resend-otp")]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequest request)
    {
        var result = await _twoFactorService.ResendOtpAsync(request);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Begin TOTP setup (Google Authenticator style).
    /// Returns the base32 secret + otpauth:// QR URI.
    /// 2FA is NOT active yet — call POST /2fa/totp/confirm to activate.
    /// </summary>
    [Authorize]
    [HttpPost("2fa/totp/setup")]
    public async Task<IActionResult> SetupTotp()
    {
        var result = await _twoFactorService.SetupTotpAsync(_currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Confirm TOTP setup by providing a valid code from the authenticator app.
    /// This activates 2FA — all future logins will require a TOTP code.
    /// </summary>
    [Authorize]
    [HttpPost("2fa/totp/confirm")]
    public async Task<IActionResult> ConfirmTotp([FromBody] ConfirmTotpRequest request)
    {
        var result = await _twoFactorService.ConfirmTotpSetupAsync(_currentUser.GetUserId(), request);
        return result.IsSuccess
            ? Ok(new { message = "TOTP two-factor authentication is now active." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Enable Email OTP as the 2FA method.
    /// Sends an OTP to the registered email to confirm delivery before activating.
    /// Complete activation by calling POST /2fa/verify with the received code.
    /// </summary>
    [Authorize]
    [HttpPost("2fa/email-otp/enable")]
    public async Task<IActionResult> EnableEmailOtp()
    {
        var result = await _twoFactorService.SendEmailOtpAsync(_currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(new { message = "OTP sent. Verify it via POST /2fa/verify to activate Email OTP.", sessionToken = result.Data?.TwoFactorSessionToken })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Disable 2FA entirely.
    /// Requires the user's current password as confirmation.
    /// </summary>
    [Authorize]
    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DisableTwoFactor([FromBody] ConfirmPasswordRequest request)
    {
        var result = await _twoFactorService.DisableTwoFactorAsync(_currentUser.GetUserId(), request.Password);
        return result.IsSuccess
            ? Ok(new { message = "Two-factor authentication disabled." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Feature 3 — Password Reset
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Request a password reset email.
    /// Always returns 200 regardless of whether the address is registered
    /// (prevents account enumeration).
    /// </summary>
    [HttpPost("password/forgot")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await _passwordResetService.SendResetEmailAsync(request, ClientIp);
        return Ok(new { message = "If that email is registered, a reset link has been sent." });
    }

    /// <summary>
    /// Reset password using the token received by email.
    /// Invalidates all existing refresh tokens to force re-login on all devices.
    /// </summary>
    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _passwordResetService.ResetPasswordAsync(request);
        return result.IsSuccess
            ? Ok(new { message = "Password reset successfully. Please log in again." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Change password while already authenticated.
    /// Requires the current password. Does not invalidate other sessions.
    /// </summary>
    [Authorize]
    [HttpPost("password/change")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var result = await _passwordResetService.ChangePasswordAsync(_currentUser.GetUserId(), request);
        return result.IsSuccess
            ? Ok(new { message = "Password changed successfully." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}

/// <summary>Simple password confirmation body used for sensitive operations.</summary>
public class ConfirmPasswordRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public string Password { get; set; } = string.Empty;
}
