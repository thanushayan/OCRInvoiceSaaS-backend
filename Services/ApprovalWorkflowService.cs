using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class ApprovalWorkflowService : IApprovalWorkflowService
{
    private readonly ApplicationDbContext _db;
    private readonly INotificationService _notifications;
    private readonly INotificationDispatcher _dispatcher;

    public ApprovalWorkflowService(ApplicationDbContext db, INotificationService notifications, INotificationDispatcher dispatcher)
    {
        _db            = db;
        _notifications = notifications;
        _dispatcher    = dispatcher;
    }

    public async Task<ServiceResult<WorkflowTemplateResponse>> CreateTemplateAsync(
        Guid companyId, CreateWorkflowTemplateRequest request, Guid userId)
    {
        bool isAdmin = await _db.CompanyUsers.AnyAsync(cu =>
            cu.CompanyId == companyId && cu.UserId == userId &&
            (cu.Role == "Owner" || cu.Role == "Admin"));

        if (!isAdmin) return ServiceResult<WorkflowTemplateResponse>.Fail("Only owners and admins can create workflow templates.", 403);

        var orders = request.Steps.Select(s => s.StepOrder).OrderBy(x => x).ToList();
        if (orders.First() != 1 || orders.Count != orders.Distinct().Count())
            return ServiceResult<WorkflowTemplateResponse>.Fail("Step orders must be unique and start at 1.", 400);

        if (request.IsDefault)
        {
            await _db.ApprovalWorkflowTemplates
                .Where(t => t.CompanyId == companyId && t.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsDefault, false));
        }

        var template = new ApprovalWorkflowTemplate
        {
            CompanyId        = companyId,
            Name             = request.Name.Trim(),
            Description      = request.Description?.Trim(),
            AmountThreshold  = request.AmountThreshold,
            IsDefault        = request.IsDefault,
            Steps            = request.Steps.Select(s => new ApprovalWorkflowStep
            {
                StepOrder      = s.StepOrder,
                StepName       = s.StepName.Trim(),
                AssignedUserId = s.AssignedUserId,
                RequiredRole   = s.RequiredRole?.Trim(),
                TimeoutHours   = s.TimeoutHours,
                IsOptional     = s.IsOptional
            }).ToList()
        };

        _db.ApprovalWorkflowTemplates.Add(template);
        await _db.SaveChangesAsync();

        return ServiceResult<WorkflowTemplateResponse>.Success(MapTemplate(template), 201);
    }

    public async Task<ServiceResult<List<WorkflowTemplateResponse>>> GetTemplatesAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<WorkflowTemplateResponse>>.Fail("Access denied.", 403);

        var templates = await _db.ApprovalWorkflowTemplates
            .Include(t => t.Steps).ThenInclude(s => s.AssignedUser)
            .Where(t => t.CompanyId == companyId && t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync();

        return ServiceResult<List<WorkflowTemplateResponse>>.Success(templates.Select(MapTemplate).ToList());
    }

    public async Task<ServiceResult> DeleteTemplateAsync(Guid templateId, Guid userId)
    {
        var template = await _db.ApprovalWorkflowTemplates
            .Include(t => t.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(t => t.Id == templateId);

        if (template == null) return ServiceResult.Fail("Template not found.", 404);

        bool isAdmin = template.Company.CompanyUsers.Any(cu =>
            cu.UserId == userId && (cu.Role == "Owner" || cu.Role == "Admin"));

        if (!isAdmin) return ServiceResult.Fail("Only owners and admins can delete templates.", 403);

        template.IsActive    = false;
        template.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult.Success(204);
    }

    public async Task<ServiceResult<ApprovalInstanceResponse>> StartApprovalAsync(
        Guid invoiceId, StartApprovalRequest request, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<ApprovalInstanceResponse>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<ApprovalInstanceResponse>.Fail("Access denied.", 403);

        bool activeExists = await _db.InvoiceApprovalInstances.AnyAsync(a =>
            a.InvoiceId == invoiceId && a.Status == ApprovalInstanceStatus.InProgress);
        if (activeExists) return ServiceResult<ApprovalInstanceResponse>.Fail("An approval workflow is already in progress for this invoice.", 409);

        var template = await _db.ApprovalWorkflowTemplates
            .Include(t => t.Steps)
            .FirstOrDefaultAsync(t => t.Id == request.WorkflowTemplateId && t.IsActive);

        if (template == null) return ServiceResult<ApprovalInstanceResponse>.Fail("Workflow template not found.", 404);

        var instance = new InvoiceApprovalInstance
        {
            InvoiceId           = invoiceId,
            WorkflowTemplateId  = template.Id,
            CurrentStepOrder    = 1,
            Status              = ApprovalInstanceStatus.InProgress,
            InitiatedByUserId   = userId
        };

        _db.InvoiceApprovalInstances.Add(instance);
        invoice.ActiveApprovalInstanceId = instance.Id;
        invoice.Status   = InvoiceStatus.Reviewed;
        invoice.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _dispatcher.ApprovalRequiredAsync(instance.Id, 1);

        return await GetInstanceAsync(instance.Id, userId);
    }

    public async Task<ServiceResult<ApprovalInstanceResponse>> GetInstanceAsync(Guid instanceId, Guid userId)
    {
        var instance = await _db.InvoiceApprovalInstances
            .Include(a => a.Invoice).ThenInclude(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .Include(a => a.WorkflowTemplate)
            .Include(a => a.Actions).ThenInclude(ac => ac.ActedByUser)
            .Include(a => a.InitiatedByUser)
            .FirstOrDefaultAsync(a => a.Id == instanceId);

        if (instance == null) return ServiceResult<ApprovalInstanceResponse>.Fail("Approval instance not found.", 404);
        if (!instance.Invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<ApprovalInstanceResponse>.Fail("Access denied.", 403);

        var currentStep = instance.WorkflowTemplate != null
            ? await _db.ApprovalWorkflowSteps
                .FirstOrDefaultAsync(s =>
                    s.WorkflowTemplateId == instance.WorkflowTemplateId &&
                    s.StepOrder == instance.CurrentStepOrder)
            : null;

        return ServiceResult<ApprovalInstanceResponse>.Success(new ApprovalInstanceResponse
        {
            Id              = instance.Id,
            InvoiceId       = instance.InvoiceId,
            InvoiceNumber   = instance.Invoice.InvoiceNumber ?? "—",
            WorkflowName    = instance.WorkflowTemplate?.Name ?? "—",
            CurrentStepOrder = instance.CurrentStepOrder,
            CurrentStepName  = currentStep?.StepName ?? "—",
            Status           = instance.Status.ToString(),
            InitiatedByName  = instance.InitiatedByUser.FullName,
            CompletedAt      = instance.CompletedAt,
            CreatedAt        = instance.CreatedAt,
            Actions          = instance.Actions
                .OrderBy(a => a.ActedAt)
                .Select(a => new ApprovalActionResponse
                {
                    Id         = a.Id,
                    StepOrder  = a.StepOrder,
                    StepName   = a.StepName,
                    ActedByName = a.ActedByUser.FullName,
                    Action     = a.Action.ToString(),
                    Comment    = a.Comment,
                    ActedAt    = a.ActedAt
                }).ToList()
        });
    }

    public async Task<ServiceResult<List<ApprovalInstanceResponse>>> GetPendingForUserAsync(Guid userId)
    {
        var companyIds = await _db.CompanyUsers
            .Where(cu => cu.UserId == userId)
            .Select(cu => cu.CompanyId)
            .ToListAsync();

        var userRole = await _db.CompanyUsers
            .Where(cu => cu.UserId == userId)
            .Select(cu => cu.Role)
            .FirstOrDefaultAsync();

        var instances = await _db.InvoiceApprovalInstances
            .Include(a => a.Invoice).ThenInclude(i => i.Company)
            .Include(a => a.WorkflowTemplate).ThenInclude(t => t.Steps)
            .Include(a => a.Actions).ThenInclude(ac => ac.ActedByUser)
            .Include(a => a.InitiatedByUser)
            .Where(a =>
                a.Status == ApprovalInstanceStatus.InProgress &&
                companyIds.Contains(a.Invoice.CompanyId))
            .ToListAsync();

        var relevant = instances.Where(a =>
        {
            var step = a.WorkflowTemplate?.Steps
                .FirstOrDefault(s => s.StepOrder == a.CurrentStepOrder);
            if (step == null) return false;
            return step.AssignedUserId == userId ||
                   (step.RequiredRole != null && step.RequiredRole == userRole);
        }).ToList();

        var results = new List<ApprovalInstanceResponse>();
        foreach (var inst in relevant)
        {
            var r = await GetInstanceAsync(inst.Id, userId);
            if (r.IsSuccess && r.Data != null) results.Add(r.Data);
        }

        return ServiceResult<List<ApprovalInstanceResponse>>.Success(results);
    }

    public async Task<ServiceResult<ApprovalInstanceResponse>> SubmitActionAsync(
        Guid instanceId, SubmitApprovalActionRequest request, Guid userId)
    {
        if (!Enum.TryParse<ApprovalStepStatus>(request.Action, true, out var action) ||
            action == ApprovalStepStatus.Pending || action == ApprovalStepStatus.Skipped)
            return ServiceResult<ApprovalInstanceResponse>.Fail("Action must be 'Approved' or 'Rejected'.", 400);

        var instance = await _db.InvoiceApprovalInstances
            .Include(a => a.Invoice).ThenInclude(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .Include(a => a.WorkflowTemplate).ThenInclude(t => t.Steps)
            .FirstOrDefaultAsync(a => a.Id == instanceId);

        if (instance == null) return ServiceResult<ApprovalInstanceResponse>.Fail("Approval not found.", 404);
        if (instance.Status != ApprovalInstanceStatus.InProgress)
            return ServiceResult<ApprovalInstanceResponse>.Fail("This approval workflow is no longer active.", 400);
        if (!instance.Invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<ApprovalInstanceResponse>.Fail("Access denied.", 403);

        var currentStep = instance.WorkflowTemplate.Steps
            .FirstOrDefault(s => s.StepOrder == instance.CurrentStepOrder);

        if (currentStep == null)
            return ServiceResult<ApprovalInstanceResponse>.Fail("Current step not found.", 500);

        _db.InvoiceApprovalActions.Add(new InvoiceApprovalAction
        {
            ApprovalInstanceId = instanceId,
            StepOrder          = currentStep.StepOrder,
            StepName           = currentStep.StepName,
            ActedByUserId      = userId,
            Action             = action,
            Comment            = request.Comment?.Trim()
        });

        if (action == ApprovalStepStatus.Rejected)
        {
            instance.Status           = ApprovalInstanceStatus.Rejected;
            instance.CompletedAt      = DateTime.UtcNow;
            instance.RejectionReason  = request.Comment;
            instance.UpdatedAt        = DateTime.UtcNow;
            instance.Invoice.Status   = InvoiceStatus.Processed;
            instance.Invoice.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var nextStep = instance.WorkflowTemplate.Steps
                .Where(s => s.StepOrder > instance.CurrentStepOrder)
                .OrderBy(s => s.StepOrder)
                .FirstOrDefault();

            if (nextStep == null)
            {
                instance.Status          = ApprovalInstanceStatus.Approved;
                instance.CompletedAt     = DateTime.UtcNow;
                instance.UpdatedAt       = DateTime.UtcNow;
                instance.Invoice.Status  = InvoiceStatus.Approved;
                instance.Invoice.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await _dispatcher.ApprovalCompletedAsync(instanceId, true);
            }
            else
            {
                instance.CurrentStepOrder = nextStep.StepOrder;
                instance.UpdatedAt        = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await _dispatcher.ApprovalRequiredAsync(instanceId, nextStep.StepOrder);
            }
        }

        await _db.SaveChangesAsync();
        return await GetInstanceAsync(instanceId, userId);
    }

    public async Task<ServiceResult> CancelApprovalAsync(Guid instanceId, Guid userId)
    {
        var instance = await _db.InvoiceApprovalInstances
            .Include(a => a.Invoice).ThenInclude(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(a => a.Id == instanceId);

        if (instance == null) return ServiceResult.Fail("Approval not found.", 404);
        if (!instance.Invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId &&
            (cu.Role == "Owner" || cu.Role == "Admin")))
            return ServiceResult.Fail("Only owners and admins can cancel approvals.", 403);

        instance.Status       = ApprovalInstanceStatus.Cancelled;
        instance.CompletedAt  = DateTime.UtcNow;
        instance.UpdatedAt    = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult.Success();
    }

    public async Task EscalateTimedOutStepsAsync()
    {
        var instances = await _db.InvoiceApprovalInstances
            .Include(a => a.WorkflowTemplate).ThenInclude(t => t.Steps)
            .Where(a => a.Status == ApprovalInstanceStatus.InProgress)
            .ToListAsync();

        foreach (var instance in instances)
        {
            var step = instance.WorkflowTemplate?.Steps
                .FirstOrDefault(s => s.StepOrder == instance.CurrentStepOrder);

            if (step == null) continue;

            var deadline = instance.UpdatedAt.AddHours(step.TimeoutHours);
            if (DateTime.UtcNow < deadline) continue;

            if (step.IsOptional)
            {
                _db.InvoiceApprovalActions.Add(new InvoiceApprovalAction
                {
                    ApprovalInstanceId = instance.Id,
                    StepOrder          = step.StepOrder,
                    StepName           = step.StepName,
                    ActedByUserId      = instance.InitiatedByUserId,
                    Action             = ApprovalStepStatus.Skipped,
                    Comment            = "Auto-skipped: step timeout exceeded."
                });

                var nextStep = instance.WorkflowTemplate.Steps
                    .Where(s => s.StepOrder > instance.CurrentStepOrder)
                    .OrderBy(s => s.StepOrder).FirstOrDefault();

                instance.CurrentStepOrder = nextStep?.StepOrder ?? instance.CurrentStepOrder;
                instance.UpdatedAt        = DateTime.UtcNow;
            }
            else
            {
                await _notifications.CreateAsync(
                    instance.InitiatedByUserId,
                    "Approval step timed out",
                    $"Step '{step.StepName}' on invoice approval has exceeded the {step.TimeoutHours}h deadline.",
                    "Warning");
            }
        }

        await _db.SaveChangesAsync();
    }

    private static WorkflowTemplateResponse MapTemplate(ApprovalWorkflowTemplate t) => new()
    {
        Id               = t.Id,
        Name             = t.Name,
        Description      = t.Description,
        AmountThreshold  = t.AmountThreshold,
        IsDefault        = t.IsDefault,
        IsActive         = t.IsActive,
        CreatedAt        = t.CreatedAt,
        Steps            = t.Steps.OrderBy(s => s.StepOrder).Select(s => new WorkflowStepResponse
        {
            Id               = s.Id,
            StepOrder        = s.StepOrder,
            StepName         = s.StepName,
            AssignedUserId   = s.AssignedUserId,
            AssignedUserName = s.AssignedUser?.FullName,
            RequiredRole     = s.RequiredRole,
            TimeoutHours     = s.TimeoutHours,
            IsOptional       = s.IsOptional
        }).ToList()
    };
}
