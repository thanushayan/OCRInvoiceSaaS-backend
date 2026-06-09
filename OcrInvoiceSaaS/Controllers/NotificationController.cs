using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/notifications")]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly CurrentUserProvider _currentUser;

    public NotificationController(INotificationService notificationService, CurrentUserProvider currentUser)
    {
        _notificationService = notificationService;
        _currentUser = currentUser;
    }

    /// <summary>Get notifications for the current user. Use ?unreadOnly=true for unread only.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool unreadOnly = false)
    {
        var userId = _currentUser.GetUserId();
        var result = await _notificationService.GetForUserAsync(userId, unreadOnly);
        return Ok(result.Data);
    }

    /// <summary>Mark notifications as read. Omit 'ids' to mark all read.</summary>
    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead([FromBody] MarkNotificationsReadRequest request)
    {
        var userId = _currentUser.GetUserId();
        var result = await _notificationService.MarkReadAsync(userId, request);
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
