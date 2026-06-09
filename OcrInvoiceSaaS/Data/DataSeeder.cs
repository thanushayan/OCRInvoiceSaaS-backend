using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Models;

namespace OcrInvoiceSaaS.Data;

public static class DataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        await SeedSubscriptionPlansAsync(db);
        await db.SaveChangesAsync();
    }

    private static async Task SeedSubscriptionPlansAsync(ApplicationDbContext db)
    {
        if (await db.SubscriptionPlans.AnyAsync()) return;

        var plans = new List<SubscriptionPlan>
        {
            new()
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Name = "Starter",
                MonthlyPrice = 49.00m,
                MaxInvoicesPerMonth = 500,
                MaxUsers = 5,
                IncludesOcr = true,
                IsActive = true
            },
            new()
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                Name = "Growth",
                MonthlyPrice = 149.00m,
                MaxInvoicesPerMonth = 5000,
                MaxUsers = 20,
                IncludesOcr = true,
                IsActive = true
            },
            new()
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                Name = "Enterprise",
                MonthlyPrice = 499.00m,
                MaxInvoicesPerMonth = int.MaxValue,
                MaxUsers = int.MaxValue,
                IncludesOcr = true,
                IsActive = true
            }
        };

        db.SubscriptionPlans.AddRange(plans);
    }

    /// <summary>
    /// Seeds default expense categories for a newly created company.
    /// Call this from CompanyService after creating a company.
    /// </summary>
    public static void SeedDefaultExpenseCategories(ApplicationDbContext db, Guid companyId)
    {
        var defaults = new[]
        {
            "Office Supplies", "Utilities", "Travel & Transport",
            "Software & Subscriptions", "Marketing & Advertising",
            "Professional Services", "Equipment & Hardware",
            "Rent & Premises", "Insurance", "Other"
        };

        foreach (var name in defaults)
        {
            db.ExpenseCategories.Add(new ExpenseCategory
            {
                CompanyId = companyId,
                Name = name
            });
        }
    }
}
