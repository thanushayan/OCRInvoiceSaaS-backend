using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Models;

namespace OcrInvoiceSaaS.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // ── Core ──────────────────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyUser> CompanyUsers => Set<CompanyUser>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<OcrProcessingLog> OcrProcessingLogs => Set<OcrProcessingLog>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<CompanySubscription> CompanySubscriptions => Set<CompanySubscription>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    // ── Security ──────────────────────────────────────────────────────────────
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserTwoFactor> UserTwoFactors => Set<UserTwoFactor>();
    public DbSet<TwoFactorSession> TwoFactorSessions => Set<TwoFactorSession>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    // ── Invoice Intelligence ──────────────────────────────────────────────────
    public DbSet<InvoiceDuplicate> InvoiceDuplicates => Set<InvoiceDuplicate>();
    public DbSet<ApprovalWorkflowTemplate> ApprovalWorkflowTemplates => Set<ApprovalWorkflowTemplate>();
    public DbSet<ApprovalWorkflowStep> ApprovalWorkflowSteps => Set<ApprovalWorkflowStep>();
    public DbSet<InvoiceApprovalInstance> InvoiceApprovalInstances => Set<InvoiceApprovalInstance>();
    public DbSet<InvoiceApprovalAction> InvoiceApprovalActions => Set<InvoiceApprovalAction>();
    public DbSet<BulkOcrJob> BulkOcrJobs => Set<BulkOcrJob>();
    public DbSet<BulkOcrJobItem> BulkOcrJobItems => Set<BulkOcrJobItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<InvoicePoMatch> InvoicePoMatches => Set<InvoicePoMatch>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    // ── Platform ──────────────────────────────────────────────────────────────
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<InvoiceActivity> InvoiceActivities => Set<InvoiceActivity>();
    public DbSet<ApiUsageRecord> ApiUsageRecords => Set<ApiUsageRecord>();
    public DbSet<DueReminderSent> DueReminderSents => Set<DueReminderSent>();

    // ── Team Workflow ─────────────────────────────────────────────────────────
    public DbSet<InvoiceMention> InvoiceMentions => Set<InvoiceMention>();
    public DbSet<InvoiceTask> InvoiceTasks => Set<InvoiceTask>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();

    // ── Integration & Compliance ──────────────────────────────────────────────
    public DbSet<AccountingConnection> AccountingConnections => Set<AccountingConnection>();
    public DbSet<InvoiceSyncRecord> InvoiceSyncRecords => Set<InvoiceSyncRecord>();
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();
    public DbSet<IpAllowlistEntry> IpAllowlistEntries => Set<IpAllowlistEntry>();
    public DbSet<DataRetentionPolicy> DataRetentionPolicies => Set<DataRetentionPolicy>();
    public DbSet<GdprErasureRequest> GdprErasureRequests => Set<GdprErasureRequest>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<InvoiceLock> InvoiceLocks => Set<InvoiceLock>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Core ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Ignore(u => u.IsLockedOut);
            e.HasOne(u => u.TwoFactor)
                .WithOne(t => t.User)
                .HasForeignKey<UserTwoFactor>(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompanyUser>(e =>
        {
            e.HasIndex(cu => new { cu.CompanyId, cu.UserId }).IsUnique();
            e.HasOne(cu => cu.Company).WithMany(c => c.CompanyUsers)
                .HasForeignKey(cu => cu.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cu => cu.User).WithMany(u => u.CompanyUsers)
                .HasForeignKey(cu => cu.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vendor>()
            .HasOne(v => v.Company).WithMany(c => c.Vendors)
            .HasForeignKey(v => v.CompanyId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Invoice>(e =>
        {
            e.HasIndex(i => new { i.CompanyId, i.InvoiceNumber });
            e.HasOne(i => i.Company).WithMany(c => c.Invoices)
                .HasForeignKey(i => i.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.UploadedByUser).WithMany()
                .HasForeignKey(i => i.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Vendor).WithMany(v => v.Invoices)
                .HasForeignKey(i => i.VendorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.ExpenseCategory).WithMany(c => c.Invoices)
                .HasForeignKey(i => i.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.Property(i => i.ExchangeRate).HasPrecision(18, 6);
        });

        modelBuilder.Entity<InvoiceItem>()
            .HasOne(ii => ii.Invoice).WithMany(i => i.InvoiceItems)
            .HasForeignKey(ii => ii.InvoiceId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OcrProcessingLog>()
            .HasOne(l => l.Invoice).WithMany(i => i.OcrProcessingLogs)
            .HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExpenseCategory>()
            .HasOne(c => c.Company).WithMany(co => co.ExpenseCategories)
            .HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CompanySubscription>(e =>
        {
            e.HasOne(cs => cs.Company).WithMany(c => c.CompanySubscriptions)
                .HasForeignKey(cs => cs.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cs => cs.SubscriptionPlan).WithMany(p => p.CompanySubscriptions)
                .HasForeignKey(cs => cs.SubscriptionPlanId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.CompanySubscription).WithMany(cs => cs.Payments)
            .HasForeignKey(p => p.CompanySubscriptionId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User).WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NotificationPreference>(e =>
        {
            e.HasIndex(p => p.UserId).IsUnique();
            e.HasOne(p => p.User).WithMany()
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.HasOne(a => a.PerformedByUser).WithMany(u => u.AuditLogs)
                .HasForeignKey(a => a.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SecurityEvent>(e =>
        {
            e.HasIndex(s => s.Timestamp);
            e.HasIndex(s => new { s.Category, s.Severity });
            e.HasOne(s => s.User).WithMany()
                .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Security ──────────────────────────────────────────────────────────
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(rt => rt.TokenHash).IsUnique();
            e.Ignore(rt => rt.IsActive);
            e.HasOne(rt => rt.User).WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TwoFactorSession>(e =>
        {
            e.HasIndex(s => s.SessionToken).IsUnique();
            e.HasOne(s => s.User).WithMany()
                .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.Ignore(t => t.IsValid);
            e.HasOne(t => t.User).WithMany()
                .HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoginAttempt>()
            .HasIndex(a => new { a.Email, a.AttemptedAt });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasIndex(k => k.KeyHash).IsUnique();
            e.Ignore(k => k.IsValid);
            e.Property(k => k.Scopes)
                .HasConversion(StringArrayConverter, StringArrayComparer);
            e.HasOne(k => k.Company).WithMany()
                .HasForeignKey(k => k.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(k => k.User).WithMany()
                .HasForeignKey(k => k.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Invoice Intelligence ──────────────────────────────────────────────
        modelBuilder.Entity<InvoiceDuplicate>(e =>
        {
            e.HasIndex(d => new { d.InvoiceId, d.DuplicateOfInvoiceId }).IsUnique();
            e.HasOne(d => d.Company).WithMany()
                .HasForeignKey(d => d.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.Invoice).WithMany(i => i.DuplicateFlags)
                .HasForeignKey(d => d.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.DuplicateOfInvoice).WithMany()
                .HasForeignKey(d => d.DuplicateOfInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.Property(d => d.MatchScore).HasPrecision(5, 2);
        });

        modelBuilder.Entity<ApprovalWorkflowTemplate>()
            .HasOne(t => t.Company).WithMany()
            .HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ApprovalWorkflowStep>(e =>
        {
            e.HasIndex(s => new { s.WorkflowTemplateId, s.StepOrder }).IsUnique();
            e.HasOne(s => s.WorkflowTemplate).WithMany(t => t.Steps)
                .HasForeignKey(s => s.WorkflowTemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.AssignedUser).WithMany()
                .HasForeignKey(s => s.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvoiceApprovalInstance>(e =>
        {
            e.HasOne(a => a.Invoice).WithMany(i => i.ApprovalInstances)
                .HasForeignKey(a => a.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.WorkflowTemplate).WithMany(t => t.Instances)
                .HasForeignKey(a => a.WorkflowTemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.InitiatedByUser).WithMany()
                .HasForeignKey(a => a.InitiatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvoiceApprovalAction>(e =>
        {
            e.HasOne(a => a.ApprovalInstance).WithMany(i => i.Actions)
                .HasForeignKey(a => a.ApprovalInstanceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.ActedByUser).WithMany()
                .HasForeignKey(a => a.ActedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BulkOcrJob>(e =>
        {
            e.HasOne(j => j.Company).WithMany()
                .HasForeignKey(j => j.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(j => j.CreatedByUser).WithMany()
                .HasForeignKey(j => j.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BulkOcrJobItem>(e =>
        {
            e.HasOne(i => i.BulkOcrJob).WithMany(j => j.Items)
                .HasForeignKey(i => i.BulkOcrJobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Invoice).WithMany(inv => inv.BulkOcrItems)
                .HasForeignKey(i => i.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrder>(e =>
        {
            e.HasIndex(p => new { p.CompanyId, p.PoNumber }).IsUnique();
            e.HasOne(p => p.Company).WithMany()
                .HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Vendor).WithMany()
                .HasForeignKey(p => p.VendorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.CreatedByUser).WithMany()
                .HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrderItem>()
            .HasOne(i => i.PurchaseOrder).WithMany(p => p.Items)
            .HasForeignKey(i => i.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InvoicePoMatch>(e =>
        {
            e.HasIndex(m => new { m.InvoiceId, m.PurchaseOrderId }).IsUnique();
            e.HasOne(m => m.Invoice).WithMany(i => i.PoMatches)
                .HasForeignKey(m => m.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.PurchaseOrder).WithMany(p => p.Matches)
                .HasForeignKey(m => m.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            e.Property(m => m.MatchScore).HasPrecision(5, 2);
        });

        modelBuilder.Entity<ExchangeRate>(e =>
        {
            e.HasIndex(r => new { r.FromCurrency, r.ToCurrency, r.FetchedAt });
            e.Property(r => r.Rate).HasPrecision(18, 6);
        });

        // ── Platform ──────────────────────────────────────────────────────────
        modelBuilder.Entity<WebhookEndpoint>(e =>
        {
            e.Property(w => w.Events)
                .HasConversion(StringArrayConverter, StringArrayComparer);
            e.HasOne(w => w.Company).WithMany()
                .HasForeignKey(w => w.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookDelivery>(e =>
        {
            e.HasIndex(d => new { d.Status, d.NextRetryAt });
            e.HasOne(d => d.WebhookEndpoint).WithMany(w => w.Deliveries)
                .HasForeignKey(d => d.WebhookEndpointId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceActivity>(e =>
        {
            e.HasOne(a => a.Invoice).WithMany()
                .HasForeignKey(a => a.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.User).WithMany()
                .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApiUsageRecord>(e =>
        {
            e.HasIndex(r => new { r.CompanyId, r.WindowKey }).IsUnique();
            e.HasOne(r => r.Company).WithMany()
                .HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DueReminderSent>(e =>
        {
            e.HasIndex(d => new { d.InvoiceId, d.DaysBeforeDue }).IsUnique();
            e.HasOne(d => d.Invoice).WithMany()
                .HasForeignKey(d => d.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Team Workflow ─────────────────────────────────────────────────────
        modelBuilder.Entity<InvoiceMention>(e =>
        {
            e.HasOne(m => m.Activity).WithMany()
                .HasForeignKey(m => m.InvoiceActivityId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Invoice).WithMany()
                .HasForeignKey(m => m.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.MentionedByUser).WithMany()
                .HasForeignKey(m => m.MentionedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.MentionedUser).WithMany()
                .HasForeignKey(m => m.MentionedUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvoiceTask>(e =>
        {
            e.HasOne(t => t.Invoice).WithMany()
                .HasForeignKey(t => t.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Company).WithMany()
                .HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.AssignedToUser).WithMany()
                .HasForeignKey(t => t.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.AssignedByUser).WithMany()
                .HasForeignKey(t => t.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovalDelegation>(e =>
        {
            e.Ignore(d => d.IsCurrentlyActive);
            e.HasOne(d => d.Company).WithMany()
                .HasForeignKey(d => d.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Delegator).WithMany()
                .HasForeignKey(d => d.DelegatorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.Delegate).WithMany()
                .HasForeignKey(d => d.DelegateUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Integration & Compliance ──────────────────────────────────────────
        modelBuilder.Entity<AccountingConnection>()
            .HasOne(c => c.Company).WithMany()
            .HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InvoiceSyncRecord>(e =>
        {
            e.HasOne(r => r.Invoice).WithMany()
                .HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.AccountingConnection).WithMany(c => c.SyncRecords)
                .HasForeignKey(r => r.AccountingConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StripeWebhookEvent>(e =>
        {
            e.HasIndex(s => s.StripeEventId).IsUnique();
            e.HasOne(s => s.Company).WithMany()
                .HasForeignKey(s => s.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IpAllowlistEntry>()
            .HasOne(i => i.Company).WithMany()
            .HasForeignKey(i => i.CompanyId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DataRetentionPolicy>(e =>
        {
            e.HasIndex(p => p.CompanyId).IsUnique();
            e.HasOne(p => p.Company).WithMany()
                .HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GdprErasureRequest>()
            .HasOne(g => g.RequestedByUser).WithMany()
            .HasForeignKey(g => g.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IdempotencyRecord>()
            .HasIndex(r => new { r.Key, r.UserId, r.RequestPath, r.RequestMethod }).IsUnique();

        modelBuilder.Entity<InvoiceLock>(e =>
        {
            e.HasIndex(l => l.InvoiceId).IsUnique();
            e.HasOne(l => l.Invoice).WithMany()
                .HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.LockedByUser).WithMany()
                .HasForeignKey(l => l.LockedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // string[] columns are stored as a single delimited string (SQL Server has no array type)
    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<string[], string> StringArrayConverter =
        new(v => string.Join(';', v), v => v.Length == 0 ? Array.Empty<string>() : v.Split(';', StringSplitOptions.RemoveEmptyEntries));

    private static readonly Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<string[]> StringArrayComparer =
        new((a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            v => v.ToArray());
}
