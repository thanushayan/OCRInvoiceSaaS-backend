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

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await ((AuthService)_authService).LoginAsync(request, ClientIp, DeviceInfo);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var result = await _authService.GetCurrentUserAsync(_currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await _refreshTokenService.RefreshAsync(request, ClientIp, DeviceInfo);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [Authorize]
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RevokeTokenRequest request)
    {
        var result = await _refreshTokenService.RevokeAsync(request.RefreshToken, _currentUser.GetUserId());
        return result.IsSuccess ? NoContent() : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [Authorize]
    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAll()
    {
        await _refreshTokenService.RevokeAllAsync(_currentUser.GetUserId());
        return NoContent();
    }

    [Authorize]
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] string? currentToken = null)
    {
        var result = await _refreshTokenService.GetActiveSessionsAsync(_currentUser.GetUserId(), currentToken);
        return Ok(result.Data);
    }

    [Authorize]
    [HttpGet("2fa/status")]
    public async Task<IActionResult> TwoFactorStatus()
    {
        var result = await _twoFactorService.GetStatusAsync(_currentUser.GetUserId());
        return Ok(result.Data);
    }

    [HttpPost("2fa/verify")]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] VerifyTwoFactorRequest request)
    {
        var result = await _twoFactorService.VerifyAndIssueTokensAsync(request, ClientIp, DeviceInfo);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPost("2fa/resend-otp")]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequest request)
    {
        var result = await _twoFactorService.ResendOtpAsync(request);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [Authorize]
    [HttpPost("2fa/totp/setup")]
    public async Task<IActionResult> SetupTotp()
    {
        var result = await _twoFactorService.SetupTotpAsync(_currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [Authorize]
    [HttpPost("2fa/totp/confirm")]
    public async Task<IActionResult> ConfirmTotp([FromBody] ConfirmTotpRequest request)
    {
        var result = await _twoFactorService.ConfirmTotpSetupAsync(_currentUser.GetUserId(), request);
        return result.IsSuccess
            ? Ok(new { message = "TOTP two-factor authentication is now active." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [Authorize]
    [HttpPost("2fa/email-otp/enable")]
    public async Task<IActionResult> EnableEmailOtp()
    {
        var result = await _twoFactorService.SendEmailOtpAsync(_currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(new { message = "OTP sent. Verify it via POST /2fa/verify to activate Email OTP.", sessionToken = result.Data?.TwoFactorSessionToken })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [Authorize]
    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DisableTwoFactor([FromBody] ConfirmPasswordRequest request)
    {
        var result = await _twoFactorService.DisableTwoFactorAsync(_currentUser.GetUserId(), request.Password);
        return result.IsSuccess
            ? Ok(new { message = "Two-factor authentication disabled." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpPost("password/forgot")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await _passwordResetService.SendResetEmailAsync(request, ClientIp);
        return Ok(new { message = "If that email is registered, a reset link has been sent." });
    }

    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _passwordResetService.ResetPasswordAsync(request);
        return result.IsSuccess
            ? Ok(new { message = "Password reset successfully. Please log in again." })
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

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

public class ConfirmPasswordRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public string Password { get; set; } = string.Empty;
}
