using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;

namespace OcrInvoiceSaaS.Services;

/// <summary>
/// Parses @mentions in invoice comments and notifies mentioned company members
/// via both in-app notification and email.
/// </summary>
public partial class MentionService : IMentionService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _email;
    private readonly INotificationService _notifications;
    private readonly IConfiguration _config;
    private readonly ILogger<MentionService> _logger;

    private string AppUrl => _config["App:BaseUrl"] ?? "https://app.ocrinvoicesaas.com";

    [GeneratedRegex(@"@([\w.+-]+(?:@[\w.-]+\.[a-z]{2,})?)", RegexOptions.IgnoreCase)]
    private static partial Regex MentionPattern();

    public MentionService(
        ApplicationDbContext db,
        IEmailService email,
        INotificationService notifications,
        IConfiguration config,
        ILogger<MentionService> logger)
    {
        _db            = db;
        _email         = email;
        _notifications = notifications;
        _config        = config;
        _logger        = logger;
    }

    // ── IMentionService ───────────────────────────────────────────────────────

    public async Task<ServiceResult<ActivityResponse>> AddCommentWithMentionsAsync(
        Guid invoiceId, AddCommentWithMentionsRequest request, Guid userId)
    {
        var invoice = await _db.Invoices.FindAsync(invoiceId);
        if (invoice == null) return ServiceResult<ActivityResponse>.Fail("Invoice not found.", 404);

        var user = await _db.Users.FindAsync(userId);
        if (user == null) return ServiceResult<ActivityResponse>.Fail("User not found.", 404);

        var activity = new InvoiceActivity
        {
            InvoiceId         = invoiceId,
            UserId            = userId,
            Type              = ActivityType.Comment,
            Comment           = request.Comment,
            IsSystemGenerated = false
        };
        _db.InvoiceActivities.Add(activity);

        // Auto-parse @tokens + explicit IDs
        var mentionedIds = new HashSet<Guid>(request.MentionedUserIds);
        var matches = MentionPattern().Matches(request.Comment);
        if (matches.Count > 0)
        {
            var members = await _db.CompanyUsers
                .Include(cu => cu.User)
                .Where(cu => cu.CompanyId == invoice.CompanyId)
                .ToListAsync();

            foreach (Match m in matches)
            {
                var token = m.Groups[1].Value.ToLower();
                var target = members.FirstOrDefault(cu =>
                    cu.User.Email.ToLower() == token ||
                    cu.User.FullName.Replace(" ", "").ToLower() == token ||
                    cu.User.FullName.Split(' ')[0].ToLower() == token);
                if (target != null) mentionedIds.Add(target.UserId);
            }
        }

        foreach (var mentionedUserId in mentionedIds.Where(id => id != userId))
        {
            _db.InvoiceMentions.Add(new InvoiceMention
            {
                InvoiceId          = invoiceId,
                InvoiceActivityId  = activity.Id,
                MentionedUserId    = mentionedUserId,
                MentionedByUserId  = userId
            });
        }

        await _db.SaveChangesAsync();

        // Fire notifications in background
        _ = Task.Run(async () =>
        {
            try { await ProcessMentionsAsync(invoiceId, invoice.CompanyId, userId, request.Comment); }
            catch { /* swallow — notification failure should not fail the comment */ }
        });

        var initials = user.FullName.Split(' ').Where(p => p.Length > 0)
            .Select(p => p[0]).Take(2).ToArray();

        return ServiceResult<ActivityResponse>.Ok(new ActivityResponse
        {
            Id                = activity.Id,
            Type              = activity.Type.ToString(),
            Comment           = activity.Comment,
            IsSystemGenerated = false,
            UserName          = user.FullName,
            UserInitials      = new string(initials).ToUpper(),
            CreatedAt         = activity.CreatedAt
        }, 201);
    }

    public async Task<ServiceResult<List<MentionResponse>>> GetUnreadMentionsAsync(Guid userId)
    {
        var mentions = await _db.InvoiceMentions
            .Include(m => m.Activity).ThenInclude(a => a.User)
            .Include(m => m.Activity).ThenInclude(a => a.Invoice)
            .Where(m => m.MentionedUserId == userId && !m.IsRead)
            .OrderByDescending(m => m.Activity.CreatedAt)
            .ToListAsync();

        var result = mentions.Select(m =>
        {
            var author   = m.Activity.User;
            var initials = author != null
                ? new string(author.FullName.Split(' ').Where(p => p.Length > 0)
                    .Select(p => p[0]).Take(2).ToArray()).ToUpper()
                : "?";
            return new MentionResponse
            {
                Id                  = m.Id,
                InvoiceId           = m.InvoiceId,
                InvoiceFileName     = m.Activity.Invoice?.FileName ?? string.Empty,
                InvoiceNumber       = m.Activity.Invoice?.InvoiceNumber,
                InvoiceActivityId   = m.InvoiceActivityId,
                CommentText         = m.Activity.Comment ?? string.Empty,
                MentionedByName     = author?.FullName ?? "Unknown",
                MentionedByInitials = initials,
                IsRead              = m.IsRead,
                CreatedAt           = m.Activity.CreatedAt,
                ReadAt              = m.ReadAt
            };
        }).ToList();

        return ServiceResult<List<MentionResponse>>.Ok(result);
    }

    public async Task<ServiceResult> MarkMentionsReadAsync(Guid userId, List<Guid>? mentionIds = null)
    {
        var query = _db.InvoiceMentions.Where(m => m.MentionedUserId == userId && !m.IsRead);
        if (mentionIds != null && mentionIds.Count > 0)
            query = query.Where(m => mentionIds.Contains(m.Id));

        await query.ExecuteUpdateAsync(s => s
            .SetProperty(m => m.IsRead, true)
            .SetProperty(m => m.ReadAt, DateTime.UtcNow));

        return ServiceResult.Ok();
    }

    public async Task<int> GetUnreadCountAsync(Guid userId) =>
        await _db.InvoiceMentions.CountAsync(m => m.MentionedUserId == userId && !m.IsRead);

    // ── Internal helpers ──────────────────────────────────────────────────────

    public async Task ProcessMentionsAsync(
        Guid invoiceId, Guid companyId, Guid authorUserId, string commentText)
    {
        var matches = MentionPattern().Matches(commentText);
        if (matches.Count == 0) return;

        var author  = await _db.Users.FindAsync(authorUserId);
        var invoice = await _db.Invoices.FindAsync(invoiceId);
        if (author == null || invoice == null) return;

        var members = await _db.CompanyUsers
            .Include(cu => cu.User)
            .Where(cu => cu.CompanyId == companyId)
            .ToListAsync();

        var notified = new HashSet<Guid>();

        foreach (Match match in matches)
        {
            var mention = match.Groups[1].Value.ToLower();

            var target = members.FirstOrDefault(m =>
                m.User.Email.ToLower() == mention ||
                m.User.FullName.Replace(" ", "").ToLower() == mention ||
                m.User.FullName.Split(' ')[0].ToLower() == mention);

            if (target == null || target.UserId == authorUserId) continue;
            if (!notified.Add(target.UserId)) continue;

            var preview = commentText.Length > 120 ? commentText[..120] + "…" : commentText;

            await _notifications.CreateAsync(
                target.UserId,
                $"{author.FullName} mentioned you",
                $"On {invoice.InvoiceNumber ?? invoice.FileName}: {preview}",
                "Info");

            try
            {
                var (subject, html) = EmailTemplates.MentionNotification(
                    target.User.FullName, author.FullName,
                    invoice.InvoiceNumber ?? invoice.FileName,
                    commentText.Length > 200 ? commentText[..200] + "…" : commentText,
                    invoiceId, AppUrl);

                await _email.SendAsync(target.User.Email, subject, html);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send mention email to {Email}", target.User.Email);
            }
        }
    }
}
