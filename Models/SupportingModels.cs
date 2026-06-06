namespace OcrInvoiceSaaS.Models;

// ══════════════════════════════════════════════════════════════════════════════
// Expense Category
// ══════════════════════════════════════════════════════════════════════════════

public class ExpenseCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Color { get; set; } = "#6366F1";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}

// ══════════════════════════════════════════════════════════════════════════════
// Subscription
// ══════════════════════════════════════════════════════════════════════════════

public class SubscriptionPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;          // Starter, Growth, Enterprise
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal AnnualPrice { get; set; }
    public int MaxUsers { get; set; } = 5;
    public int MaxInvoicesPerMonth { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CompanySubscription> CompanySubscriptions { get; set; } = new List<CompanySubscription>();
}

public class CompanySubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime? EndDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company Company { get; set; } = null!;
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanySubscriptionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GBP";
    public string Status { get; set; } = "Pending";
    public string? TransactionId { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;

    public CompanySubscription CompanySubscription { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════════════════════
// Notification
// ══════════════════════════════════════════════════════════════════════════════

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "Info"; // Info, Success, Warning, Error
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
