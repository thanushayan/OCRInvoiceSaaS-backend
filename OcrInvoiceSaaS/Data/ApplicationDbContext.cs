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
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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
    public DbSet<InvoiceCurrencyConversion> InvoiceCurrencyConversions => Set<InvoiceCurrencyConversion>();
    public DbSet<CompanyCurrencySetting> CompanyCurrencySettings => Set<CompanyCurrencySetting>();

    // ── Team & Workflow ───────────────────────────────────────────────────────
    public DbSet<InvoiceMention> InvoiceMentions => Set<InvoiceMention>();
    public DbSet<InvoiceTask> InvoiceTasks => Set<InvoiceTask>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();

    // ── Platform ──────────────────────────────────────────────────────────────
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<InvoiceActivity> InvoiceActivities => Set<InvoiceActivity>();
    public DbSet<ApiUsageRecord> ApiUsageRecords => Set<ApiUsageRecord>();
    public DbSet<DueReminderSent> DueRemindersSent => Set<DueReminderSent>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    // ── Integrations & Compliance ─────────────────────────────────────────────
    public DbSet<AccountingConnection> AccountingConnections => Set<AccountingConnection>();
    public DbSet<InvoiceSyncRecord> InvoiceSyncRecords => Set<InvoiceSyncRecord>();
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();
    public DbSet<IpAllowlistEntry> IpAllowlistEntries => Set<IpAllowlistEntry>();
    public DbSet<DataRetentionPolicy> DataRetentionPolicies => Set<DataRetentionPolicy>();
    public DbSet<GdprErasureRequest> GdprErasureRequests => Set<GdprErasureRequest>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<InvoiceLock> InvoiceLocks => Set<InvoiceLock>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Ignore(x => x.IsLockedOut);
        });

        modelBuilder.Entity<Company>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.RegistrationNumber).HasMaxLength(50);
            e.Property(x => x.VatNumber).HasMaxLength(50);
        });

        modelBuilder.Entity<CompanyUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CompanyId, x.UserId }).IsUnique();
            e.Property(x => x.Role).HasMaxLength(50).IsRequired();
            e.HasOne(x => x.Company).WithMany(c => c.CompanyUsers)
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany(u => u.CompanyUsers)
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Vendor>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.HasOne(x => x.Company).WithMany(c => c.Vendors)
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invoice>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            e.Property(x => x.FileUrl).HasMaxLength(2000).IsRequired();
            e.Property(x => x.FileType).HasMaxLength(20).IsRequired();
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.TaxAmount).HasPrecision(18, 2);
            e.Property(x => x.SubTotal).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Company).WithMany(c => c.Invoices)
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.UploadedByUser).WithMany(u => u.UploadedInvoices)
             .HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Vendor).WithMany(v => v.Invoices)
             .HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ExpenseCategory).WithMany(ec => ec.Invoices)
             .HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InvoiceItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(500).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.Property(x => x.TaxRate).HasPrecision(5, 2);
            e.HasOne(x => x.Invoice).WithMany(i => i.InvoiceItems)
             .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OcrProcessingLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.HasOne(x => x.Invoice).WithMany(i => i.OcrProcessingLogs)
             .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExpenseCategory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Company).WithMany(c => c.ExpenseCategories)
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubscriptionPlan>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.MonthlyPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<CompanySubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(50);
            e.HasOne(x => x.Company).WithMany(c => c.CompanySubscriptions)
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SubscriptionPlan).WithMany(sp => sp.CompanySubscriptions)
             .HasForeignKey(x => x.SubscriptionPlanId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(50);
            e.HasOne(x => x.CompanySubscription).WithMany(cs => cs.Payments)
             .HasForeignKey(x => x.CompanySubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Type).HasMaxLength(50);
            e.HasOne(x => x.User).WithMany(u => u.Notifications)
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.Timestamp);
            e.HasOne(x => x.PerformedByUser).WithMany()
             .HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // ══ Security ════════════════════════════════════════════════════════

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.DeviceInfo).HasMaxLength(500);
            e.Property(x => x.IpAddress).HasMaxLength(50);
            e.Property(x => x.ReplacedByTokenId).HasMaxLength(100);
            e.HasIndex(x => new { x.UserId, x.IsRevoked, x.IsUsed });
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserTwoFactor>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TotpSecretKey).HasMaxLength(200);
            e.Property(x => x.EmailOtpCodeHash).HasMaxLength(200);
            e.HasOne(x => x.User).WithOne(u => u.TwoFactor)
             .HasForeignKey<UserTwoFactor>(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TwoFactorSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SessionToken).IsUnique();
            e.Property(x => x.SessionToken).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.User).WithMany()
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash);
            e.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.IpAddress).HasMaxLength(50);
            e.HasOne(x => x.User).WithMany()
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoginAttempt>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Email, x.AttemptedAt });
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
            e.Property(x => x.IpAddress).HasMaxLength(50);
            e.Property(x => x.FailureReason).HasMaxLength(200);
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.Property(x => x.KeyHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.KeyPrefix).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.LastUsedIp).HasMaxLength(50);
            e.Property(x => x.Scopes)
             .HasConversion(
                 v => string.Join(',', v),
                 v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
            e.HasOne(x => x.User).WithMany(u => u.ApiKeys)
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Company).WithMany()
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        // ══ Invoice Intelligence ════════════════════════════════════════════

        modelBuilder.Entity<InvoiceDuplicate>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.InvoiceId, x.DuplicateOfInvoiceId }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.Status });
            e.Property(x => x.MatchReason).HasMaxLength(200);
            e.Property(x => x.MatchScore).HasPrecision(5, 2);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Invoice).WithMany(i => i.DuplicateFlags).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DuplicateOfInvoice).WithMany().HasForeignKey(x => x.DuplicateOfInvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovalWorkflowTemplate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.AmountThreshold).HasPrecision(18, 2);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalWorkflowStep>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WorkflowTemplateId, x.StepOrder }).IsUnique();
            e.Property(x => x.StepName).HasMaxLength(100).IsRequired();
            e.Property(x => x.RequiredRole).HasMaxLength(50);
            e.HasOne(x => x.WorkflowTemplate).WithMany(t => t.Steps).HasForeignKey(x => x.WorkflowTemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InvoiceApprovalInstance>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.InvoiceId, x.Status });
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Invoice).WithMany(i => i.ApprovalInstances).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.WorkflowTemplate).WithMany(t => t.Instances).HasForeignKey(x => x.WorkflowTemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InitiatedByUser).WithMany().HasForeignKey(x => x.InitiatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvoiceApprovalAction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.StepName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Action).HasConversion<string>();
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasOne(x => x.ApprovalInstance).WithMany(a => a.Actions).HasForeignKey(x => x.ApprovalInstanceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ActedByUser).WithMany().HasForeignKey(x => x.ActedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BulkOcrJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CompanyId, x.Status });
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BulkOcrJobItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BulkOcrJobId, x.Status });
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.ErrorMessage).HasMaxLength(500);
            e.HasOne(x => x.BulkOcrJob).WithMany(j => j.Items).HasForeignKey(x => x.BulkOcrJobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Invoice).WithMany(i => i.BulkOcrItems).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrder>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CompanyId, x.PoNumber });
            e.Property(x => x.PoNumber).HasMaxLength(100).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrderItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(500).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.Property(x => x.ReceivedQuantity).HasPrecision(18, 4);
            e.HasOne(x => x.PurchaseOrder).WithMany(p => p.Items).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoicePoMatch>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.InvoiceId, x.PurchaseOrderId }).IsUnique();
            e.Property(x => x.MatchScore).HasPrecision(5, 2);
            e.Property(x => x.AmountVariance).HasPrecision(18, 2);
            e.Property(x => x.AmountVariancePercent).HasPrecision(8, 2);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.MatchNotes).HasMaxLength(500);
            e.HasOne(x => x.Invoice).WithMany(i => i.PoMatches).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PurchaseOrder).WithMany(p => p.Matches).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExchangeRate>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.FromCurrency, x.ToCurrency, x.FetchedAt });
            e.Property(x => x.FromCurrency).HasMaxLength(10).IsRequired();
            e.Property(x => x.ToCurrency).HasMaxLength(10).IsRequired();
            e.Property(x => x.Rate).HasPrecision(18, 6);
            e.Property(x => x.Source).HasMaxLength(50);
        });

        modelBuilder.Entity<InvoiceCurrencyConversion>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Invoice)
             .WithMany()
             .HasForeignKey(x => x.InvoiceId)
             .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.ExchangeRate)
             .WithMany()
             .HasForeignKey(x => x.ExchangeRateId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.OriginalCurrency).HasMaxLength(10).IsRequired();
            e.Property(x => x.BaseCurrency).HasMaxLength(10).IsRequired();
            e.Property(x => x.OriginalAmount).HasPrecision(18, 4);
            e.Property(x => x.ConvertedAmount).HasPrecision(18, 4);
            e.Property(x => x.RateUsed).HasPrecision(18, 6);
        });

        modelBuilder.Entity<CompanyCurrencySetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Company)
             .WithMany()
             .HasForeignKey(x => x.CompanyId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CompanyId).IsUnique();
            e.Property(x => x.BaseCurrency).HasMaxLength(10).IsRequired();
        });

        // ══ Team & Workflow ════════════════════════════════════════════════

        modelBuilder.Entity<InvoiceMention>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MentionedUserId, x.IsRead });
            e.HasIndex(x => x.InvoiceActivityId);
            e.HasOne(x => x.Activity).WithMany()
             .HasForeignKey(x => x.InvoiceActivityId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Invoice).WithMany()
             .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MentionedByUser).WithMany()
             .HasForeignKey(x => x.MentionedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MentionedUser).WithMany()
             .HasForeignKey(x => x.MentionedUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvoiceTask>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AssignedToUserId, x.Status });
            e.HasIndex(x => new { x.CompanyId, x.Status });
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Priority).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.CompletionNote).HasMaxLength(500);
            e.HasOne(x => x.Invoice).WithMany()
             .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Company).WithMany()
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedToUser).WithMany()
             .HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedByUser).WithMany()
             .HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovalDelegation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CompanyId, x.DelegatorUserId, x.IsActive });
            e.Property(x => x.Reason).HasMaxLength(200);
            e.Ignore(x => x.IsCurrentlyActive);
            e.HasOne(x => x.Company).WithMany()
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Delegator).WithMany()
             .HasForeignKey(x => x.DelegatorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Delegate).WithMany()
             .HasForeignKey(x => x.DelegateUserId).OnDelete(DeleteBehavior.Restrict);
        });
        // ══ Platform ════════════════════════════════════════════════════════

        modelBuilder.Entity<WebhookEndpoint>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.SecretHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.SecretPrefix).HasMaxLength(10);
            e.Property(x => x.Events)
             .HasConversion(v => string.Join(',', v), v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
            e.HasOne(x => x.Company).WithMany()
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookDelivery>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WebhookEndpointId, x.Status, x.NextRetryAt });
            e.Property(x => x.EventName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.ResponseBody).HasMaxLength(500);
            e.Property(x => x.ErrorMessage).HasMaxLength(500);
            e.HasOne(x => x.WebhookEndpoint).WithMany(w => w.Deliveries)
             .HasForeignKey(x => x.WebhookEndpointId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceActivity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.InvoiceId, x.CreatedAt });
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne(x => x.Invoice).WithMany()
             .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ApiUsageRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CompanyId, x.WindowKey }).IsUnique();
            e.Property(x => x.WindowKey).HasMaxLength(50).IsRequired();
            e.HasOne(x => x.Company).WithMany()
             .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DueReminderSent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.InvoiceId, x.DaysBeforeDue }).IsUnique();
            e.HasOne(x => x.Invoice).WithMany()
             .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationPreference>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithMany()
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ══ Integrations & Compliance ═══════════════════════════════════════

        modelBuilder.Entity<AccountingConnection>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CompanyId, x.Provider });
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TenantId).HasMaxLength(100).IsRequired();
            e.Property(x => x.TenantName).HasMaxLength(200);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceSyncRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.InvoiceId, x.AccountingConnectionId });
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AccountingConnection).WithMany(c => c.SyncRecords)
             .HasForeignKey(x => x.AccountingConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StripeWebhookEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StripeEventId).IsUnique();
            e.Property(x => x.StripeEventId).HasMaxLength(100).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(100);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IpAllowlistEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CompanyId, x.CidrRange });
            e.Property(x => x.CidrRange).HasMaxLength(50).IsRequired();
            e.Property(x => x.Description).HasMaxLength(100);
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DataRetentionPolicy>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CompanyId).IsUnique();
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GdprErasureRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RequestedByUserId, x.Status });
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Key, x.UserId, x.RequestPath });
            e.Property(x => x.Key).HasMaxLength(200).IsRequired();
            e.Property(x => x.RequestPath).HasMaxLength(500);
            e.Property(x => x.RequestMethod).HasMaxLength(10);
        });

        modelBuilder.Entity<InvoiceLock>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.InvoiceId).IsUnique();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.LockedByUser).WithMany().HasForeignKey(x => x.LockedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
