using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;
using TaskStatus = OcrInvoiceSaaS.Models.TaskStatus;

namespace OcrInvoiceSaaS.Services;

public class TaskService : ITaskService
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notifications;

    public TaskService(ApplicationDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<ServiceResult<TaskResponse>> CreateAsync(
        Guid invoiceId, CreateTaskRequest request, Guid assignedByUserId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<TaskResponse>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == assignedByUserId))
            return ServiceResult<TaskResponse>.Fail("Access denied.", 403);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == request.AssignedToUserId))
            return ServiceResult<TaskResponse>.Fail("Assignee is not a member of this company.", 400);

        if (!Enum.TryParse<TaskPriority>(request.Priority, true, out var priority))
            priority = TaskPriority.Medium;

        var task = new InvoiceTask
        {
            InvoiceId = invoiceId,
            CompanyId = invoice.CompanyId,
            AssignedToUserId = request.AssignedToUserId,
            AssignedByUserId = assignedByUserId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Priority = priority,
            DueDate = request.DueDate
        };

        _db.InvoiceTasks.Add(task);
        await _db.SaveChangesAsync();

        await _notifications.CreateAsync(
            request.AssignedToUserId,
            "Task assigned to you",
            $"'{task.Title}' on invoice {invoice.InvoiceNumber ?? invoice.FileName}.",
            "Info");

        return ServiceResult<TaskResponse>.Success(await MapAsync(task.Id), 201);
    }

    public async Task<ServiceResult<TaskResponse>> UpdateAsync(Guid taskId, UpdateTaskRequest request, Guid userId)
    {
        var task = await _db.InvoiceTasks
            .Include(t => t.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null) return ServiceResult<TaskResponse>.Fail("Task not found.", 404);
        if (!task.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<TaskResponse>.Fail("Access denied.", 403);

        if (!string.IsNullOrWhiteSpace(request.Title)) task.Title = request.Title.Trim();
        if (request.Description != null) task.Description = request.Description.Trim();
        if (request.DueDate.HasValue) task.DueDate = request.DueDate;

        if (request.AssignedToUserId.HasValue && request.AssignedToUserId != task.AssignedToUserId)
        {
            if (!task.Company.CompanyUsers.Any(cu => cu.UserId == request.AssignedToUserId.Value))
                return ServiceResult<TaskResponse>.Fail("Assignee is not a member of this company.", 400);
            task.AssignedToUserId = request.AssignedToUserId.Value;
            await _notifications.CreateAsync(task.AssignedToUserId, "Task assigned to you", $"'{task.Title}' was reassigned to you.", "Info");
        }

        if (!string.IsNullOrWhiteSpace(request.Priority) &&
            Enum.TryParse<TaskPriority>(request.Priority, true, out var priority))
            task.Priority = priority;

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<TaskStatus>(request.Status, true, out var status))
                return ServiceResult<TaskResponse>.Fail(
                    $"Status must be one of: {string.Join(", ", Enum.GetNames<TaskStatus>())}.", 400);

            task.Status = status;
            if (status == TaskStatus.Completed)
            {
                task.CompletedAt = DateTime.UtcNow;
                task.CompletionNote = request.CompletionNote?.Trim();
            }
        }

        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult<TaskResponse>.Success(await MapAsync(task.Id));
    }

    public async Task<ServiceResult<List<TaskResponse>>> GetForInvoiceAsync(Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<List<TaskResponse>>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<List<TaskResponse>>.Fail("Access denied.", 403);

        var ids = await _db.InvoiceTasks
            .Where(t => t.InvoiceId == invoiceId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.Id)
            .ToListAsync();

        return ServiceResult<List<TaskResponse>>.Success(await MapManyAsync(ids));
    }

    public async Task<ServiceResult<List<TaskResponse>>> GetAssignedToUserAsync(Guid userId, TaskFilterRequest filter)
    {
        var q = _db.InvoiceTasks.Where(t => t.AssignedToUserId == userId);
        q = ApplyFilter(q, filter);

        var ids = await q.OrderByDescending(t => t.CreatedAt).Select(t => t.Id).ToListAsync();
        return ServiceResult<List<TaskResponse>>.Success(await MapManyAsync(ids));
    }

    public async Task<ServiceResult<List<TaskResponse>>> GetForCompanyAsync(
        Guid companyId, TaskFilterRequest filter, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<TaskResponse>>.Fail("Access denied.", 403);

        var q = _db.InvoiceTasks.Where(t => t.CompanyId == companyId);
        q = ApplyFilter(q, filter);

        var ids = await q.OrderByDescending(t => t.CreatedAt).Select(t => t.Id).ToListAsync();
        return ServiceResult<List<TaskResponse>>.Success(await MapManyAsync(ids));
    }

    public async Task<ServiceResult> DeleteAsync(Guid taskId, Guid userId)
    {
        var task = await _db.InvoiceTasks
            .Include(t => t.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null) return ServiceResult.Fail("Task not found.", 404);

        bool canDelete = task.AssignedByUserId == userId ||
            task.Company.CompanyUsers.Any(cu => cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));
        if (!canDelete) return ServiceResult.Fail("Only the task creator or an admin can delete tasks.", 403);

        _db.InvoiceTasks.Remove(task);
        await _db.SaveChangesAsync();

        return ServiceResult.Success(204);
    }

    public async Task NotifyOverdueTasksAsync()
    {
        var now = DateTime.UtcNow;

        var overdue = await _db.InvoiceTasks
            .Include(t => t.Invoice)
            .Where(t =>
                t.DueDate != null && t.DueDate < now &&
                (t.Status == TaskStatus.Open || t.Status == TaskStatus.InProgress))
            .ToListAsync();

        foreach (var task in overdue)
        {
            await _notifications.CreateAsync(
                task.AssignedToUserId,
                "Task overdue",
                $"'{task.Title}' on invoice {task.Invoice.InvoiceNumber ?? task.Invoice.FileName} was due {task.DueDate:yyyy-MM-dd}.",
                "Warning");
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static IQueryable<InvoiceTask> ApplyFilter(IQueryable<InvoiceTask> q, TaskFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Status) &&
            Enum.TryParse<TaskStatus>(filter.Status, true, out var status))
            q = q.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(filter.Priority) &&
            Enum.TryParse<TaskPriority>(filter.Priority, true, out var priority))
            q = q.Where(t => t.Priority == priority);

        if (filter.AssignedToUserId.HasValue)
            q = q.Where(t => t.AssignedToUserId == filter.AssignedToUserId.Value);

        if (filter.OverdueOnly)
            q = q.Where(t =>
                t.DueDate != null && t.DueDate < DateTime.UtcNow &&
                (t.Status == TaskStatus.Open || t.Status == TaskStatus.InProgress));

        return q;
    }

    private async Task<List<TaskResponse>> MapManyAsync(List<Guid> ids)
    {
        var results = new List<TaskResponse>();
        foreach (var id in ids) results.Add(await MapAsync(id));
        return results;
    }

    private async Task<TaskResponse> MapAsync(Guid taskId)
    {
        var t = await _db.InvoiceTasks
            .Include(x => x.Invoice)
            .Include(x => x.AssignedToUser)
            .Include(x => x.AssignedByUser)
            .FirstAsync(x => x.Id == taskId);

        return new TaskResponse
        {
            Id = t.Id,
            InvoiceId = t.InvoiceId,
            InvoiceFileName = t.Invoice.FileName,
            InvoiceNumber = t.Invoice.InvoiceNumber,
            Title = t.Title,
            Description = t.Description,
            Priority = t.Priority.ToString(),
            Status = t.Status.ToString(),
            DueDate = t.DueDate,
            IsOverdue = t.DueDate.HasValue && t.DueDate < DateTime.UtcNow &&
                        t.Status is TaskStatus.Open or TaskStatus.InProgress,
            AssignedToUserId = t.AssignedToUserId,
            AssignedToName = t.AssignedToUser.FullName,
            AssignedToInitials = Initials(t.AssignedToUser.FullName),
            AssignedByName = t.AssignedByUser.FullName,
            CompletedAt = t.CompletedAt,
            CompletionNote = t.CompletionNote,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };
    }

    private static string Initials(string fullName)
        => string.Concat(fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(p => char.ToUpper(p[0])));
}
