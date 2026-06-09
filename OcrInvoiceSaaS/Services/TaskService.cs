using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class TaskService : ITaskService
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IEmailService _email;

    public TaskService(
        ApplicationDbContext db,
        INotificationService notifications,
        IEmailService email)
    {
        _db            = db;
        _notifications = notifications;
        _email         = email;
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

        if (!Enum.TryParse<TaskPriority>(request.Priority, true, out var priority))
            return ServiceResult<TaskResponse>.Fail("Priority must be Low, Medium, High, or Urgent.", 400);

        // Verify assignee belongs to the same company
        bool assigneeBelongs = invoice.Company.CompanyUsers
            .Any(cu => cu.UserId == request.AssignedToUserId);

        if (!assigneeBelongs)
            return ServiceResult<TaskResponse>.Fail("Assigned user is not a member of this company.", 400);

        var task = new InvoiceTask
        {
            InvoiceId          = invoiceId,
            CompanyId          = invoice.CompanyId,
            AssignedToUserId   = request.AssignedToUserId,
            AssignedByUserId   = assignedByUserId,
            Title              = request.Title.Trim(),
            Description        = request.Description?.Trim(),
            Priority           = priority,
            Status             = TaskStatus.Open,
            DueDate            = request.DueDate
        };

        _db.InvoiceTasks.Add(task);
        await _db.SaveChangesAsync();

        // Notify assignee
        var assigner = await _db.Users.FindAsync(assignedByUserId);
        var assignee = await _db.Users.FindAsync(request.AssignedToUserId);

        if (assigner != null && assignee != null)
        {
            await _notifications.CreateAsync(
                request.AssignedToUserId,
                $"Task assigned by {assigner.FullName}",
                $"\"{task.Title}\" on invoice {invoice.InvoiceNumber ?? invoice.FileName}",
                priority == TaskPriority.Urgent ? "Warning" : "Info");

            await _email.SendAsync(
                to: assignee.Email,
                subject: $"Task assigned to you: {task.Title}",
                body: $@"
                    <p>Hi {assignee.FullName},</p>
                    <p><strong>{assigner.FullName}</strong> has assigned you a task on invoice
                    <strong>{invoice.InvoiceNumber ?? invoice.FileName}</strong>:</p>
                    <table>
                      <tr><td><strong>Task:</strong></td><td>{task.Title}</td></tr>
                      {(task.Description != null ? $"<tr><td><strong>Details:</strong></td><td>{task.Description}</td></tr>" : "")}
                      <tr><td><strong>Priority:</strong></td><td>{task.Priority}</td></tr>
                      {(task.DueDate.HasValue ? $"<tr><td><strong>Due:</strong></td><td>{task.DueDate:dd MMM yyyy}</td></tr>" : "")}
                    </table>
                    <p><a href=""https://app.ocrinvoicesaas.com/invoices/{invoiceId}"">View Invoice →</a></p>");
        }

        return ServiceResult<TaskResponse>.Success(await BuildTaskResponseAsync(task.Id), 201);
    }

    public async Task<ServiceResult<TaskResponse>> UpdateAsync(
        Guid taskId, UpdateTaskRequest request, Guid userId)
    {
        var task = await _db.InvoiceTasks
            .Include(t => t.Invoice).ThenInclude(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null) return ServiceResult<TaskResponse>.Fail("Task not found.", 404);

        bool isMember = task.Invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId);
        if (!isMember) return ServiceResult<TaskResponse>.Fail("Access denied.", 403);

        // Reassignment — only assigner or admin/owner can reassign
        if (request.AssignedToUserId.HasValue &&
            request.AssignedToUserId != task.AssignedToUserId)
        {
            bool canReassign = task.AssignedByUserId == userId ||
                               task.Invoice.Company.CompanyUsers.Any(cu =>
                                   cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));

            if (!canReassign)
                return ServiceResult<TaskResponse>.Fail("Only the assigner or an admin can reassign tasks.", 403);

            // Notify new assignee
            var newAssignee = await _db.Users.FindAsync(request.AssignedToUserId);
            var reassigner  = await _db.Users.FindAsync(userId);
            if (newAssignee != null && reassigner != null)
            {
                await _notifications.CreateAsync(
                    request.AssignedToUserId.Value,
                    $"Task reassigned to you by {reassigner.FullName}",
                    $"\"{task.Title}\" on invoice {task.Invoice.InvoiceNumber ?? task.Invoice.FileName}",
                    "Info");
            }

            task.AssignedToUserId = request.AssignedToUserId.Value;
        }

        if (request.Title != null) task.Title = request.Title.Trim();
        if (request.Description != null) task.Description = request.Description.Trim();

        if (request.Priority != null && Enum.TryParse<TaskPriority>(request.Priority, true, out var priority))
            task.Priority = priority;

        if (request.Status != null && Enum.TryParse<TaskStatus>(request.Status, true, out var status))
        {
            task.Status = status;
            if (status == TaskStatus.Completed && task.CompletedAt == null)
            {
                task.CompletedAt = DateTime.UtcNow;

                // Notify the person who assigned the task
                var completer = await _db.Users.FindAsync(userId);
                if (completer != null && task.AssignedByUserId != userId)
                {
                    await _notifications.CreateAsync(
                        task.AssignedByUserId,
                        $"Task completed by {completer.FullName}",
                        $"\"{task.Title}\" on invoice {task.Invoice.InvoiceNumber ?? task.Invoice.FileName}",
                        "Success");
                }
            }
        }

        if (request.DueDate.HasValue) task.DueDate = request.DueDate;
        if (request.CompletionNote != null) task.CompletionNote = request.CompletionNote.Trim();

        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult<TaskResponse>.Success(await BuildTaskResponseAsync(taskId));
    }

    public async Task<ServiceResult<List<TaskResponse>>> GetForInvoiceAsync(Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<List<TaskResponse>>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<List<TaskResponse>>.Fail("Access denied.", 403);

        var taskIds = await _db.InvoiceTasks
            .Where(t => t.InvoiceId == invoiceId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.Id)
            .ToListAsync();

        var responses = new List<TaskResponse>();
        foreach (var id in taskIds)
            responses.Add(await BuildTaskResponseAsync(id));

        return ServiceResult<List<TaskResponse>>.Success(responses);
    }

    public async Task<ServiceResult<List<TaskResponse>>> GetAssignedToUserAsync(
        Guid userId, TaskFilterRequest filter)
    {
        var query = _db.InvoiceTasks.Where(t => t.AssignedToUserId == userId);
        query = ApplyFilter(query, filter);

        var taskIds = await query
            .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenByDescending(t => t.Priority)
            .Select(t => t.Id)
            .ToListAsync();

        var responses = new List<TaskResponse>();
        foreach (var id in taskIds)
            responses.Add(await BuildTaskResponseAsync(id));

        return ServiceResult<List<TaskResponse>>.Success(responses);
    }

    public async Task<ServiceResult<List<TaskResponse>>> GetForCompanyAsync(
        Guid companyId, TaskFilterRequest filter, Guid userId)
    {
        bool isMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<TaskResponse>>.Fail("Access denied.", 403);

        var query = _db.InvoiceTasks.Where(t => t.CompanyId == companyId);
        query = ApplyFilter(query, filter);

        var taskIds = await query
            .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenByDescending(t => t.Priority)
            .Select(t => t.Id)
            .ToListAsync();

        var responses = new List<TaskResponse>();
        foreach (var id in taskIds)
            responses.Add(await BuildTaskResponseAsync(id));

        return ServiceResult<List<TaskResponse>>.Success(responses);
    }

    public async Task<ServiceResult> DeleteAsync(Guid taskId, Guid userId)
    {
        var task = await _db.InvoiceTasks
            .Include(t => t.Invoice).ThenInclude(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null) return ServiceResult.Fail("Task not found.", 404);

        bool canDelete = task.AssignedByUserId == userId ||
                         task.Invoice.Company.CompanyUsers.Any(cu =>
                             cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));

        if (!canDelete)
            return ServiceResult.Fail("Only the assigner or an admin can delete tasks.", 403);

        _db.InvoiceTasks.Remove(task);
        await _db.SaveChangesAsync();

        return ServiceResult.Success(204);
    }

    public async Task NotifyOverdueTasksAsync()
    {
        var overdue = await _db.InvoiceTasks
            .Include(t => t.AssignedToUser)
            .Include(t => t.Invoice)
            .Where(t =>
                t.DueDate.HasValue &&
                t.DueDate.Value < DateTime.UtcNow &&
                t.Status == TaskStatus.Open || t.Status == TaskStatus.InProgress)
            .ToListAsync();

        foreach (var task in overdue)
        {
            await _notifications.CreateAsync(
                task.AssignedToUserId,
                "Overdue task",
                $"\"{task.Title}\" on invoice {task.Invoice.InvoiceNumber ?? task.Invoice.FileName} was due {task.DueDate:dd MMM}.",
                "Warning");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IQueryable<InvoiceTask> ApplyFilter(IQueryable<InvoiceTask> q, TaskFilterRequest f)
    {
        if (!string.IsNullOrWhiteSpace(f.Status) &&
            Enum.TryParse<TaskStatus>(f.Status, true, out var status))
            q = q.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(f.Priority) &&
            Enum.TryParse<TaskPriority>(f.Priority, true, out var priority))
            q = q.Where(t => t.Priority == priority);

        if (f.AssignedToUserId.HasValue)
            q = q.Where(t => t.AssignedToUserId == f.AssignedToUserId.Value);

        if (f.OverdueOnly)
            q = q.Where(t => t.DueDate.HasValue && t.DueDate.Value < DateTime.UtcNow
                && t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled);

        return q;
    }

    private async Task<TaskResponse> BuildTaskResponseAsync(Guid taskId)
    {
        var t = await _db.InvoiceTasks
            .Include(x => x.Invoice)
            .Include(x => x.AssignedToUser)
            .Include(x => x.AssignedByUser)
            .FirstAsync(x => x.Id == taskId);

        return new TaskResponse
        {
            Id               = t.Id,
            InvoiceId        = t.InvoiceId,
            InvoiceFileName  = t.Invoice.FileName,
            InvoiceNumber    = t.Invoice.InvoiceNumber,
            Title            = t.Title,
            Description      = t.Description,
            Priority         = t.Priority.ToString(),
            Status           = t.Status.ToString(),
            DueDate          = t.DueDate,
            IsOverdue        = t.DueDate.HasValue && t.DueDate.Value < DateTime.UtcNow
                                && t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled,
            AssignedToUserId    = t.AssignedToUserId,
            AssignedToName      = t.AssignedToUser.FullName,
            AssignedToInitials  = BuildInitials(t.AssignedToUser.FullName),
            AssignedByName      = t.AssignedByUser.FullName,
            CompletedAt         = t.CompletedAt,
            CompletionNote      = t.CompletionNote,
            CreatedAt           = t.CreatedAt,
            UpdatedAt           = t.UpdatedAt
        };
    }

    private static string BuildInitials(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[^1][0]}".ToUpper()
            : fullName[..Math.Min(2, fullName.Length)].ToUpper();
    }
}
