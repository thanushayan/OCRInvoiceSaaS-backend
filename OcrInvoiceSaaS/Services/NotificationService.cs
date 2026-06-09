using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;

    public NotificationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceResult<List<NotificationResponse>>> GetForUserAsync(Guid userId, bool unreadOnly = false)
    {
        var query = _db.Notifications
            .Where(n => n.UserId == userId);

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationResponse
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Type = n.Type,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        return ServiceResult<List<NotificationResponse>>.Success(notifications);
    }

    public async Task<ServiceResult> MarkReadAsync(Guid userId, MarkNotificationsReadRequest request)
    {
        IQueryable<Notification> query = _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead);

        // If specific IDs provided, filter to those; otherwise mark all
        if (request.Ids != null && request.Ids.Count > 0)
            query = query.Where(n => request.Ids.Contains(n.Id));

        await query.ExecuteUpdateAsync(setters =>
            setters.SetProperty(n => n.IsRead, true));

        return ServiceResult.Success();
    }

    public async Task CreateAsync(Guid userId, string title, string message, string type = "Info")
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type
        });

        await _db.SaveChangesAsync();
    }
}
