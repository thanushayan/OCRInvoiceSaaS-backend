using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;

namespace OcrInvoiceSaaS.Services;

public partial class MentionService
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
        _db = db;
        _email = email;
        _notifications = notifications;
        _config = config;
        _logger = logger;
    }

    public async Task ProcessMentionsAsync(
        Guid invoiceId, Guid companyId, Guid authorUserId, string commentText)
    {
        var matches = MentionPattern().Matches(commentText);
        if (matches.Count == 0) return;

        var author = await _db.Users.FindAsync(authorUserId);
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

            var preview = commentText.Length > 120 ? commentText[..120] + "..." : commentText;

            await _notifications.CreateAsync(
                target.UserId,
                $"{author.FullName} mentioned you",
                $"On {invoice.InvoiceNumber ?? invoice.FileName}: {preview}",
                "Info");

            try
            {
                await _email.SendAsync(
                    target.User.Email,
                    $"{author.FullName} mentioned you in a comment",
                    $"<p>{author.FullName} mentioned you: {preview}</p>");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send mention email to {Email}", target.User.Email);
            }
        }
    }
}
