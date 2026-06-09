using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

// ══════════════════════════════════════════════════════════════════════════════
// Purchase Order Service
// ══════════════════════════════════════════════════════════════════════════════

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly ApplicationDbContext _db;

    public PurchaseOrderService(ApplicationDbContext db) => _db = db;

    private async Task<bool> IsMemberAsync(Guid companyId, Guid userId)
        => await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);

    public async Task<ServiceResult<PurchaseOrderResponse>> CreateAsync(
        Guid companyId, CreatePurchaseOrderRequest request, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<PurchaseOrderResponse>.Fail("Access denied.", 403);

        bool duplicate = await _db.PurchaseOrders.AnyAsync(p =>
            p.CompanyId == companyId &&
            p.PoNumber.ToLower() == request.PoNumber.ToLower().Trim());
        if (duplicate)
            return ServiceResult<PurchaseOrderResponse>.Fail("A PO with this number already exists.", 409);

        var po = new PurchaseOrder
        {
            CompanyId            = companyId,
            VendorId             = request.VendorId,
            PoNumber             = request.PoNumber.Trim(),
            PoDate               = request.PoDate,
            ExpectedDeliveryDate = request.ExpectedDeliveryDate,
            Currency             = request.Currency.ToUpper(),
            Notes                = request.Notes?.Trim(),
            CreatedByUserId      = userId,
            Status               = PoStatus.Draft
        };

        foreach (var item in request.Items)
        {
            var lineTotal = Math.Round(item.Quantity * item.UnitPrice, 2);
            po.Items.Add(new PurchaseOrderItem
            {
                Description = item.Description.Trim(),
                Quantity    = item.Quantity,
                UnitPrice   = item.UnitPrice,
                LineTotal   = lineTotal
            });
        }

        po.TotalAmount = po.Items.Sum(i => i.LineTotal);

        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        return ServiceResult<PurchaseOrderResponse>.Success(await MapPoAsync(po.Id), 201);
    }

    public async Task<ServiceResult<List<PurchaseOrderResponse>>> GetByCompanyAsync(Guid companyId, Guid userId)
    {
        if (!await IsMemberAsync(companyId, userId))
            return ServiceResult<List<PurchaseOrderResponse>>.Fail("Access denied.", 403);

        var ids = await _db.PurchaseOrders
            .Where(p => p.CompanyId == companyId && p.Status != PoStatus.Cancelled)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.Id)
            .ToListAsync();

        var results = new List<PurchaseOrderResponse>();
        foreach (var id in ids) results.Add(await MapPoAsync(id));

        return ServiceResult<List<PurchaseOrderResponse>>.Success(results);
    }

    public async Task<ServiceResult<PurchaseOrderResponse>> GetByIdAsync(Guid poId, Guid userId)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(p => p.Id == poId);

        if (po == null) return ServiceResult<PurchaseOrderResponse>.Fail("PO not found.", 404);
        if (!po.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<PurchaseOrderResponse>.Fail("Access denied.", 403);

        return ServiceResult<PurchaseOrderResponse>.Success(await MapPoAsync(poId));
    }

    public async Task<ServiceResult> DeleteAsync(Guid poId, Guid userId)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(p => p.Id == poId);

        if (po == null) return ServiceResult.Fail("PO not found.", 404);
        if (!po.Company.CompanyUsers.Any(cu => cu.UserId == userId &&
            (cu.Role == "Owner" || cu.Role == "Admin")))
            return ServiceResult.Fail("Only owners and admins can delete POs.", 403);

        po.Status    = PoStatus.Cancelled;
        po.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult.Success(204);
    }

    private async Task<PurchaseOrderResponse> MapPoAsync(Guid poId)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.Items)
            .Include(p => p.Vendor)
            .Include(p => p.Matches).ThenInclude(m => m.Invoice)
            .FirstAsync(p => p.Id == poId);

        return new PurchaseOrderResponse
        {
            Id                   = po.Id,
            PoNumber             = po.PoNumber,
            PoDate               = po.PoDate,
            ExpectedDeliveryDate = po.ExpectedDeliveryDate,
            TotalAmount          = po.TotalAmount,
            Currency             = po.Currency,
            Status               = po.Status.ToString(),
            VendorName           = po.Vendor?.Name,
            Notes                = po.Notes,
            CreatedAt            = po.CreatedAt,
            Items = po.Items.Select(i => new PoItemResponse
            {
                Id               = i.Id,
                Description      = i.Description,
                Quantity         = i.Quantity,
                UnitPrice        = i.UnitPrice,
                LineTotal        = i.LineTotal,
                ReceivedQuantity = i.ReceivedQuantity
            }).ToList(),
            Matches = po.Matches.Select(m => new PoMatchSummary
            {
                MatchId              = m.Id,
                InvoiceId            = m.InvoiceId,
                InvoiceNumber        = m.Invoice.InvoiceNumber ?? "—",
                InvoiceAmount        = m.Invoice.TotalAmount,
                MatchScore           = m.MatchScore,
                Status               = m.Status.ToString(),
                AmountVariance       = m.AmountVariance,
                AmountVariancePercent = m.AmountVariancePercent
            }).ToList()
        };
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Invoice Matching Service
// ══════════════════════════════════════════════════════════════════════════════

public class InvoiceMatchingService : IInvoiceMatchingService
{
    private readonly ApplicationDbContext _db;

    public InvoiceMatchingService(ApplicationDbContext db) => _db = db;

    public async Task<ServiceResult<List<PoMatchResponse>>> AutoMatchAsync(Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<List<PoMatchResponse>>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<List<PoMatchResponse>>.Fail("Access denied.", 403);

        // Candidate POs: same company, not fully matched, same currency
        var candidatePOs = await _db.PurchaseOrders
            .Include(p => p.Vendor)
            .Where(p =>
                p.CompanyId == invoice.CompanyId &&
                p.Status != PoStatus.FullyMatched &&
                p.Status != PoStatus.Cancelled &&
                p.Currency == (invoice.Currency ?? "GBP"))
            .ToListAsync();

        var newMatches = new List<InvoicePoMatch>();

        foreach (var po in candidatePOs)
        {
            var (score, notes) = ScorePoMatch(invoice, po);
            if (score < 50m) continue;

            bool alreadyMatched = await _db.InvoicePoMatches.AnyAsync(m =>
                m.InvoiceId == invoiceId && m.PurchaseOrderId == po.Id);
            if (alreadyMatched) continue;

            decimal? variance = null, variancePct = null;
            if (invoice.TotalAmount.HasValue && po.TotalAmount > 0)
            {
                variance    = invoice.TotalAmount.Value - po.TotalAmount;
                variancePct = Math.Round(variance.Value / po.TotalAmount * 100, 2);
            }

            var match = new InvoicePoMatch
            {
                InvoiceId             = invoiceId,
                PurchaseOrderId       = po.Id,
                MatchScore            = score,
                Status                = score >= 80m ? PoMatchStatus.Matched : PoMatchStatus.PartialMatch,
                AmountVariance        = variance,
                AmountVariancePercent = variancePct,
                MatchNotes            = notes,
                IsManual              = false,
                MatchedByUserId       = userId
            };

            newMatches.Add(match);
            _db.InvoicePoMatches.Add(match);

            // Update PO status
            if (score >= 80m) po.Status = PoStatus.FullyMatched;
            else if (po.Status == PoStatus.Draft || po.Status == PoStatus.Sent)
                po.Status = PoStatus.PartiallyMatched;
        }

        await _db.SaveChangesAsync();

        return ServiceResult<List<PoMatchResponse>>.Success(newMatches.Select(MapMatch).ToList());
    }

    public async Task<ServiceResult<PoMatchResponse>> ManualMatchAsync(
        Guid invoiceId, MatchInvoiceToPoRequest request, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<PoMatchResponse>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<PoMatchResponse>.Fail("Access denied.", 403);

        var po = await _db.PurchaseOrders
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseOrderId && p.CompanyId == invoice.CompanyId);
        if (po == null) return ServiceResult<PoMatchResponse>.Fail("Purchase order not found.", 404);

        bool exists = await _db.InvoicePoMatches.AnyAsync(m =>
            m.InvoiceId == invoiceId && m.PurchaseOrderId == po.Id);
        if (exists) return ServiceResult<PoMatchResponse>.Fail("Invoice is already matched to this PO.", 409);

        decimal? variance = null, variancePct = null;
        if (invoice.TotalAmount.HasValue && po.TotalAmount > 0)
        {
            variance    = invoice.TotalAmount.Value - po.TotalAmount;
            variancePct = Math.Round(variance.Value / po.TotalAmount * 100, 2);
        }

        var match = new InvoicePoMatch
        {
            InvoiceId             = invoiceId,
            PurchaseOrderId       = po.Id,
            MatchScore            = 100m,
            Status                = PoMatchStatus.Matched,
            AmountVariance        = variance,
            AmountVariancePercent = variancePct,
            MatchNotes            = "Manually confirmed by user.",
            IsManual              = true,
            MatchedByUserId       = userId
        };

        _db.InvoicePoMatches.Add(match);
        po.Status    = PoStatus.FullyMatched;
        po.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult<PoMatchResponse>.Success(MapMatch(match), 201);
    }

    public async Task<ServiceResult<List<PoMatchResponse>>> GetMatchesForInvoiceAsync(Guid invoiceId, Guid userId)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return ServiceResult<List<PoMatchResponse>>.Fail("Invoice not found.", 404);
        if (!invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult<List<PoMatchResponse>>.Fail("Access denied.", 403);

        var matches = await _db.InvoicePoMatches
            .Include(m => m.PurchaseOrder)
            .Where(m => m.InvoiceId == invoiceId)
            .OrderByDescending(m => m.MatchScore)
            .ToListAsync();

        return ServiceResult<List<PoMatchResponse>>.Success(matches.Select(MapMatch).ToList());
    }

    public async Task<ServiceResult> DismissMatchAsync(Guid matchId, Guid userId)
    {
        var match = await _db.InvoicePoMatches
            .Include(m => m.Invoice).ThenInclude(i => i.Company).ThenInclude(c => c.CompanyUsers)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) return ServiceResult.Fail("Match not found.", 404);
        if (!match.Invoice.Company.CompanyUsers.Any(cu => cu.UserId == userId))
            return ServiceResult.Fail("Access denied.", 403);

        match.Status = PoMatchStatus.Unmatched;
        await _db.SaveChangesAsync();

        return ServiceResult.Success(204);
    }

    // ── Matching engine ───────────────────────────────────────────────────────

    private static (decimal score, string notes) ScorePoMatch(Invoice invoice, PurchaseOrder po)
    {
        decimal score = 0;
        var notes     = new List<string>();

        // Vendor match
        if (invoice.VendorId.HasValue && invoice.VendorId == po.VendorId)
        {
            score += 40;
            notes.Add("Vendor matched");
        }
        else if (!string.IsNullOrWhiteSpace(invoice.ExtractedVendorName) && po.Vendor != null &&
                 po.Vendor.Name.Contains(invoice.ExtractedVendorName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
            notes.Add("Vendor name partial match");
        }

        // Amount within 5% tolerance
        if (invoice.TotalAmount.HasValue && po.TotalAmount > 0)
        {
            var diff = Math.Abs(invoice.TotalAmount.Value - po.TotalAmount);
            var pct  = diff / po.TotalAmount;

            if (pct <= 0.01m) { score += 40; notes.Add("Amount exact match"); }
            else if (pct <= 0.05m) { score += 25; notes.Add("Amount near match (<5%)"); }
            else if (pct <= 0.10m) { score += 10; notes.Add("Amount loose match (<10%)"); }
        }

        // PO number referenced in OCR text
        if (!string.IsNullOrWhiteSpace(invoice.RawOcrText) &&
            invoice.RawOcrText.Contains(po.PoNumber, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
            notes.Add("PO number found in OCR text");
        }

        return (Math.Min(score, 100), string.Join("; ", notes));
    }

    private static PoMatchResponse MapMatch(InvoicePoMatch m) => new()
    {
        Id                    = m.Id,
        InvoiceId             = m.InvoiceId,
        PurchaseOrderId       = m.PurchaseOrderId,
        PoNumber              = m.PurchaseOrder?.PoNumber ?? "—",
        MatchScore            = m.MatchScore,
        Status                = m.Status.ToString(),
        AmountVariance        = m.AmountVariance,
        AmountVariancePercent = m.AmountVariancePercent,
        MatchNotes            = m.MatchNotes,
        IsManual              = m.IsManual,
        MatchedAt             = m.MatchedAt
    };
}
