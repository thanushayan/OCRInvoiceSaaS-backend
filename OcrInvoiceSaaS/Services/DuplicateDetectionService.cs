using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class DuplicateDetectionService : IDuplicateDetectionService
{
    private readonly ApplicationDbContext _db;

    // Scoring weights — total = 100
    private const decimal WeightInvoiceNumber = 40m;
    private const decimal WeightVendor        = 30m;
    private const decimal WeightAmount        = 20m;
    private const decimal WeightDate          = 10m;
    private const decimal FlagThreshold       = 60m; // flag if score >= this

    public DuplicateDetectionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DuplicateCheckResult> CheckAsync(Guid invoiceId, Guid companyId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Vendor)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return new DuplicateCheckResult();

        // Candidates: all other processed invoices for the same company
        var candidates = await _db.Invoices
            .Include(i => i.Vendor)
            .Where(i =>
                i.CompanyId == companyId &&
                i.Id != invoiceId &&
                i.Status >= InvoiceStatus.Processed)
            .ToListAsync();

        var duplicates = new List<InvoiceDuplicate>();

        foreach (var candidate in candidates)
        {
            var (score, reason) = ScoreMatch(invoice, candidate);

            if (score < FlagThreshold) continue;

            // Avoid creating duplicate flags that already exist
            bool alreadyFlagged = await _db.InvoiceDuplicates.AnyAsync(d =>
                d.InvoiceId == invoiceId && d.DuplicateOfInvoiceId == candidate.Id);

            if (alreadyFlagged) continue;

            var dup = new InvoiceDuplicate
            {
                CompanyId            = companyId,
                InvoiceId            = invoiceId,
                DuplicateOfInvoiceId = candidate.Id,
                MatchReason          = reason,
                MatchScore           = score,
                Status               = DuplicateStatus.Flagged
            };

            duplicates.Add(dup);
            _db.InvoiceDuplicates.Add(dup);
        }

        if (duplicates.Count > 0)
        {
            invoice.IsDuplicateFlagged = true;
            await _db.SaveChangesAsync();
            // Fire notification for the first (highest-scoring) duplicate
            await _dispatcher.DuplicateFlaggedAsync(invoiceId, duplicates[0].Id);
        }

        return new DuplicateCheckResult
        {
            HasDuplicates = duplicates.Count > 0,
            Duplicates    = duplicates.Select(d => new DuplicateFlagResponse
            {
                Id                         = d.Id,
                InvoiceId                  = d.InvoiceId,
                DuplicateOfInvoiceId       = d.DuplicateOfInvoiceId,
                DuplicateOfInvoiceNumber   = candidates.First(c => c.Id == d.DuplicateOfInvoiceId).InvoiceNumber ?? "—",
                DuplicateOfFileName        = candidates.First(c => c.Id == d.DuplicateOfInvoiceId).FileName,
                DuplicateOfInvoiceDate     = candidates.First(c => c.Id == d.DuplicateOfInvoiceId).InvoiceDate,
                DuplicateOfAmount          = candidates.First(c => c.Id == d.DuplicateOfInvoiceId).TotalAmount,
                MatchReason                = d.MatchReason,
                MatchScore                 = d.MatchScore,
                Status                     = d.Status.ToString(),
                DetectedAt                 = d.DetectedAt
            }).ToList()
        };
    }

    public async Task<ServiceResult<List<DuplicateFlagResponse>>> GetFlagsForCompanyAsync(Guid companyId, Guid userId)
    {
        bool isMember = await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);
        if (!isMember) return ServiceResult<List<DuplicateFlagResponse>>.Fail("Access denied.", 403);

        var flags = await _db.InvoiceDuplicates
            .Include(d => d.Invoice)
            .Include(d => d.DuplicateOfInvoice)
            .Where(d => d.CompanyId == companyId && d.Status == DuplicateStatus.Flagged)
            .OrderByDescending(d => d.MatchScore)
            .Select(d => new DuplicateFlagResponse
            {
                Id                       = d.Id,
                InvoiceId                = d.InvoiceId,
                DuplicateOfInvoiceId     = d.DuplicateOfInvoiceId,
                DuplicateOfInvoiceNumber = d.DuplicateOfInvoice.InvoiceNumber ?? "—",
                DuplicateOfFileName      = d.DuplicateOfInvoice.FileName,
                DuplicateOfInvoiceDate   = d.DuplicateOfInvoice.InvoiceDate,
                DuplicateOfAmount        = d.DuplicateOfInvoice.TotalAmount,
                MatchReason              = d.MatchReason,
                MatchScore               = d.MatchScore,
                Status                   = d.Status.ToString(),
                DetectedAt               = d.DetectedAt
            })
            .ToListAsync();

        return ServiceResult<List<DuplicateFlagResponse>>.Success(flags);
    }

    public async Task<ServiceResult<List<DuplicateFlagResponse>>> GetFlagsForInvoiceAsync(Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<List<DuplicateFlagResponse>>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<List<DuplicateFlagResponse>>.Fail("Access denied.", 403);

        var flags = await _db.InvoiceDuplicates
            .Include(d => d.DuplicateOfInvoice)
            .Where(d => d.InvoiceId == invoiceId)
            .Select(d => new DuplicateFlagResponse
            {
                Id                       = d.Id,
                InvoiceId                = d.InvoiceId,
                DuplicateOfInvoiceId     = d.DuplicateOfInvoiceId,
                DuplicateOfInvoiceNumber = d.DuplicateOfInvoice.InvoiceNumber ?? "—",
                DuplicateOfFileName      = d.DuplicateOfInvoice.FileName,
                DuplicateOfInvoiceDate   = d.DuplicateOfInvoice.InvoiceDate,
                DuplicateOfAmount        = d.DuplicateOfInvoice.TotalAmount,
                MatchReason              = d.MatchReason,
                MatchScore               = d.MatchScore,
                Status                   = d.Status.ToString(),
                DetectedAt               = d.DetectedAt
            })
            .ToListAsync();

        return ServiceResult<List<DuplicateFlagResponse>>.Success(flags);
    }

    public async Task<ServiceResult> ReviewFlagAsync(Guid duplicateFlagId, ReviewDuplicateRequest request, Guid userId)
    {
        var flag = await _db.InvoiceDuplicates
            .Include(d => d.Invoice).ThenInclude(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(d => d.Id == duplicateFlagId);

        if (flag == null) return ServiceResult.Fail("Duplicate flag not found.", 404);
        if (!flag.Invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult.Fail("Access denied.", 403);

        if (!Enum.TryParse<DuplicateStatus>(request.Status, true, out var newStatus))
            return ServiceResult.Fail("Status must be 'Dismissed' or 'Confirmed'.", 400);

        flag.Status           = newStatus;
        flag.ReviewedByUserId = userId;
        flag.ReviewedAt       = DateTime.UtcNow;
        flag.ReviewNote       = request.Note;

        // If dismissed, clear the flag on the invoice (if no other active flags remain)
        if (newStatus == DuplicateStatus.Dismissed)
        {
            bool otherFlags = await _db.InvoiceDuplicates.AnyAsync(d =>
                d.InvoiceId == flag.InvoiceId && d.Id != duplicateFlagId && d.Status == DuplicateStatus.Flagged);

            if (!otherFlags)
                flag.Invoice.IsDuplicateFlagged = false;
        }

        await _db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    // ── Scoring engine ────────────────────────────────────────────────────────

    private static (decimal score, string reason) ScoreMatch(Invoice a, Invoice b)
    {
        decimal score = 0m;
        var reasons  = new List<string>();

        // Invoice number exact match (highest weight)
        if (!string.IsNullOrWhiteSpace(a.InvoiceNumber) &&
            !string.IsNullOrWhiteSpace(b.InvoiceNumber) &&
            string.Equals(a.InvoiceNumber.Trim(), b.InvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += WeightInvoiceNumber;
            reasons.Add("InvoiceNumber");
        }

        // Vendor match (vendor ID or extracted name)
        bool sameVendorId   = a.VendorId.HasValue && a.VendorId == b.VendorId;
        bool sameVendorName = !string.IsNullOrWhiteSpace(a.ExtractedVendorName) &&
                              !string.IsNullOrWhiteSpace(b.ExtractedVendorName) &&
                              string.Equals(
                                  a.ExtractedVendorName.Trim(),
                                  b.ExtractedVendorName.Trim(),
                                  StringComparison.OrdinalIgnoreCase);

        if (sameVendorId || sameVendorName)
        {
            score += WeightVendor;
            reasons.Add("Vendor");
        }

        // Amount match (within 1% tolerance for rounding differences)
        if (a.TotalAmount.HasValue && b.TotalAmount.HasValue && b.TotalAmount.Value != 0)
        {
            var diff = Math.Abs(a.TotalAmount.Value - b.TotalAmount.Value);
            var pct  = diff / Math.Abs(b.TotalAmount.Value);
            if (pct <= 0.01m)
            {
                score += WeightAmount;
                reasons.Add("Amount");
            }
        }

        // Date match (same calendar date)
        if (a.InvoiceDate.HasValue && b.InvoiceDate.HasValue &&
            a.InvoiceDate.Value.Date == b.InvoiceDate.Value.Date)
        {
            score += WeightDate;
            reasons.Add("Date");
        }

        return (score, string.Join("+", reasons));
    }
}
