using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Controllers;

// ══════════════════════════════════════════════════════════════════════════════
// Notification Preferences
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/me/notification-preferences")]
public class NotificationPreferencesController : ControllerBase
{
    private readonly NotificationPreferenceService _service;
    private readonly CurrentUserProvider _currentUser;

    public NotificationPreferencesController(
        NotificationPreferenceService service, CurrentUserProvider currentUser)
    {
        _service     = service;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Get the current user's notification preferences.
    /// Shows which channels (email / in-app) are active for each event type.
    /// Defaults: all on except WeeklyDigestEmail (opt-in).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var result = await _service.GetAsync(_currentUser.GetUserId());
        return Ok(result.Data);
    }

    /// <summary>
    /// Update notification preferences. Only send fields you want to change.
    /// Set WeeklyDigestEmail=true to opt in to Monday morning digests.
    /// </summary>
    [HttpPatch]
    public async Task<IActionResult> Update([FromBody] UpdateNotificationPreferenceRequest request)
    {
        var result = await _service.UpdateAsync(_currentUser.GetUserId(), request);
        return Ok(result.Data);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Email Provider Test (admin utility)
// ══════════════════════════════════════════════════════════════════════════════

[Authorize]
[ApiController]
[Route("api/admin/email")]
public class EmailTestController : ControllerBase
{
    private readonly IEmailService _email;
    private readonly CurrentUserProvider _currentUser;

    public EmailTestController(IEmailService email, CurrentUserProvider currentUser)
    {
        _email       = email;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Send a test email to the authenticated user's address.
    /// Useful for verifying SendGrid / Mailgun configuration.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest()
    {
        // Get current user's email from claims
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Could not determine email from token." });

        var html = EmailTemplates.Render(
            $@"<h2>Test Email</h2>
               <p>This is a test email from OCR Invoice SaaS.</p>
               <p>If you received this, your email provider is configured correctly.</p>
               <div class='detail'>
                 <table>
                   <tr><td>Sent at</td><td>{DateTime.UtcNow:dd MMM yyyy HH:mm} UTC</td></tr>
                   <tr><td>Recipient</td><td>{email}</td></tr>
                 </table>
               </div>");

        await _email.SendAsync(email, "Test email from OCR Invoice SaaS", html);

        return Ok(new { message = $"Test email sent to {email}." });
    }
}
