using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class BulkOcrService : IBulkOcrService
{
    private readonly ApplicationDbContext _db;
    private readonly IOcrService _ocrService;
    private readonly INotificationService _notifications;
    private readonly ILogger<BulkOcrService> _logger;
    private const int BatchSize = 10;
    private readonly INotificationDispatcher _dispatcher;

    public BulkOcrService(
        ApplicationDbContext db,
        IOcrService ocrService,
        INotificationService notifications,
        INotificationDispatcher dispatcher,
        ILogger<BulkOcrService> logger)
    {
        _db = db;
        _ocrService = ocrService;
        _notifications = notifications;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<ServiceResult<BulkOcrJobResponse>> CreateJobAsync(
        Guid companyId, CreateBulkOcrJobRequest request, Guid userId)
    {
        bool isMember = await _db.CompanyUsers
            .AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<BulkOcrJobResponse>.Fail("Access denied.", 403);

        if (request.InvoiceIds.Count > 100)
            return ServiceResult<BulkOcrJobResponse>.Fail("Maximum 100 invoices per bulk job.", 400);

        var validInvoices = await _db.Invoices
            .Where(i =>
                request.InvoiceIds.Contains(i.Id) &&
                i.CompanyId == companyId &&
                (i.Status == InvoiceStatus.Uploaded || i.Status == InvoiceStatus.Failed))
            .Select(i => i.Id)
            .ToListAsync();

        if (validInvoices.Count == 0)
            return ServiceResult<BulkOcrJobResponse>.Fail("No eligible invoices found.", 400);

        var job = new BulkOcrJob
        {
            CompanyId = companyId,
            CreatedByUserId = userId,
            TotalItems = validInvoices.Count,
            Status = BulkJobStatus.Queued
        };

        _db.BulkOcrJobs.Add(job);

        int position = 1;
        foreach (var invoiceId in validInvoices)
        {
            _db.BulkOcrJobItems.Add(new BulkOcrJobItem
            {
                BulkOcrJobId = job.Id,
                InvoiceId = invoiceId,
                QueuePosition = position++,
                Status = BulkJobItemStatus.Queued
            });
        }

        await _db.SaveChangesAsync();

        return ServiceResult<BulkOcrJobResponse>.Success(await BuildJobResponseAsync(job.Id), 201);
    }

    public async Task<ServiceResult<BulkOcrJobResponse>> GetJobAsync(Guid jobId, Guid userId)
    {
        var job = await _db.BulkOcrJobs
            .Include(j => j.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(j => j.Id == jobId);

        if (job == null) return ServiceResult<BulkOcrJobResponse>.Fail("Job not found.", 404);
        if (!job.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<BulkOcrJobResponse>.Fail("Access denied.", 403);

        return ServiceResult<BulkOcrJobResponse>.Success(await BuildJobResponseAsync(jobId));
    }

    public async Task<ServiceResult<List<BulkOcrJobResponse>>> GetJobsForCompanyAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<BulkOcrJobResponse>>.Fail("Access denied.", 403);

        var jobIds = await _db.BulkOcrJobs
            .Where(j => j.CompanyId == companyId)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => j.Id)
            .Take(50)
            .ToListAsync();

        var responses = new List<BulkOcrJobResponse>();
        foreach (var id in jobIds)
            responses.Add(await BuildJobResponseAsync(id));

        return ServiceResult<List<BulkOcrJobResponse>>.Success(responses);
    }

    public async Task<ServiceResult> CancelJobAsync(Guid jobId, Guid userId)
    {
        var job = await _db.BulkOcrJobs
            .Include(j => j.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(j => j.Id == jobId);

        if (job == null) return ServiceResult.Fail("Job not found.", 404);
        if (!job.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult.Fail("Access denied.", 403);

        if (job.Status == BulkJobStatus.Completed || job.Status == BulkJobStatus.Failed)
            return ServiceResult.Fail("Cannot cancel a completed or failed job.", 400);

        await _db.BulkOcrJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BulkJobStatus.Failed));

        await _db.BulkOcrJobItems
            .Where(i => i.BulkOcrJobId == jobId && i.Status == BulkJobItemStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, BulkJobItemStatus.Failed)
                .SetProperty(i => i.ErrorMessage, "Job cancelled by user."));

        return ServiceResult.Success();
    }

    public async Task ProcessPendingJobsAsync()
    {
        var job = await _db.BulkOcrJobs
            .FirstOrDefaultAsync(j => j.Status == BulkJobStatus.Queued || j.Status == BulkJobStatus.Processing);

        if (job == null) return;

        if (job.Status == BulkJobStatus.Queued)
        {
            job.Status = BulkJobStatus.Processing;
            job.StartedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var items = await _db.BulkOcrJobItems
            .Where(i => i.BulkOcrJobId == job.Id && i.Status == BulkJobItemStatus.Queued)
            .OrderBy(i => i.QueuePosition)
            .Take(BatchSize)
            .ToListAsync();

        foreach (var item in items)
        {
            item.Status = BulkJobItemStatus.Processing;
            await _db.SaveChangesAsync();

            var start = DateTime.UtcNow;
            var ocrResult = await _ocrService.ProcessInvoiceAsync(item.InvoiceId, job.CreatedByUserId);

            item.DurationMs = (int)(DateTime.UtcNow - start).TotalMilliseconds;
            item.ProcessedAt = DateTime.UtcNow;

            if (ocrResult.IsSuccess)
            {
                item.Status = BulkJobItemStatus.Completed;
                job.ProcessedItems++;
            }
            else
            {
                item.Status = BulkJobItemStatus.Failed;
                item.ErrorMessage = ocrResult.Error;
                job.FailedItems++;
            }

            await _db.SaveChangesAsync();
        }

        bool anyQueued = await _db.BulkOcrJobItems
            .AnyAsync(i => i.BulkOcrJobId == job.Id && i.Status == BulkJobItemStatus.Queued);

        if (!anyQueued)
        {
            job.Status = job.FailedItems == 0 ? BulkJobStatus.Completed
                        : job.ProcessedItems == 0 ? BulkJobStatus.Failed
                        : BulkJobStatus.PartiallyFailed;
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _dispatcher.BulkOcrCompletedAsync(job.Id);
        }
    }

    private async Task<BulkOcrJobResponse> BuildJobResponseAsync(Guid jobId)
    {
        var job = await _db.BulkOcrJobs
            .Include(j => j.Items).ThenInclude(i => i.Invoice)
            .FirstAsync(j => j.Id == jobId);

        var pending = job.Items.Count(i => i.Status == BulkJobItemStatus.Queued || i.Status == BulkJobItemStatus.Processing);
        var progress = job.TotalItems > 0
            ? Math.Round((double)(job.ProcessedItems + job.FailedItems) / job.TotalItems * 100, 1)
            : 0;

        return new BulkOcrJobResponse
        {
            Id = job.Id,
            Status = job.Status.ToString(),
            TotalItems = job.TotalItems,
            ProcessedItems = job.ProcessedItems,
            FailedItems = job.FailedItems,
            PendingItems = pending,
            ProgressPercent = progress,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            CreatedAt = job.CreatedAt,
            Items = job.Items.OrderBy(i => i.QueuePosition).Select(i => new BulkOcrJobItemResponse
            {
                Id = i.Id,
                InvoiceId = i.InvoiceId,
                FileName = i.Invoice.FileName,
                QueuePosition = i.QueuePosition,
                Status = i.Status.ToString(),
                ErrorMessage = i.ErrorMessage,
                DurationMs = i.DurationMs,
                ProcessedAt = i.ProcessedAt
            }).ToList()
        };
    }
}
