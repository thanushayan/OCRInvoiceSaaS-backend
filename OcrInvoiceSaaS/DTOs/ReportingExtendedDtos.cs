using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

// ══════════════════════════════════════════════════════════════════════════════
// PDF Report Requests
// ══════════════════════════════════════════════════════════════════════════════

public class MonthlySpendReportRequest
{
    [Required, Range(2000, 2100)]
    public int Year { get; set; }

    [Required, Range(1, 12)]
    public int Month { get; set; }

    public string? Currency { get; set; } = "GBP";
    public bool IncludeVendorBreakdown { get; set; } = true;
    public bool IncludeCategoryBreakdown { get; set; } = true;
    public bool IncludeInvoiceList { get; set; } = true;
}

public class VatReportPdfRequest
{
    [Required]
    public DateTime PeriodStart { get; set; }

    [Required]
    public DateTime PeriodEnd { get; set; }

    public string VatScheme { get; set; } = "Standard";  // Standard | FlatRate | CashAccounting
    public string? CompanyVatNumber { get; set; }
}

// ══════════════════════════════════════════════════════════════════════════════
// MTD (Making Tax Digital) VAT Return
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// HMRC MTD VAT Return — 9 mandatory boxes as per VAT Notice 700.
/// https://www.gov.uk/guidance/submit-vat-returns-using-making-tax-digital-for-vat
/// </summary>
public class MtdVatReturnResponse
{
    // ── Identification ────────────────────────────────────────────────────────
    public Guid CompanyId { get; set; }
    public string? VatRegistrationNumber { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string PeriodKey { get; set; } = string.Empty;   // e.g. "24AA" for HMRC submission
    public string VatScheme { get; set; } = "Standard";
    public DateTime CalculatedAt { get; set; }

    // ── The 9 VAT return boxes ────────────────────────────────────────────────
    /// <summary>Box 1 — VAT due on sales and other outputs (20% standard-rated)</summary>
    public decimal Box1VatDueOnSales { get; set; }

    /// <summary>Box 2 — VAT due on acquisitions from EC member states (post-Brexit = 0)</summary>
    public decimal Box2VatDueOnAcquisitions { get; set; } = 0m;

    /// <summary>Box 3 — Total VAT due (Box 1 + Box 2)</summary>
    public decimal Box3TotalVatDue => Box1VatDueOnSales + Box2VatDueOnAcquisitions;

    /// <summary>Box 4 — VAT reclaimed on purchases (input tax on invoices in the system)</summary>
    public decimal Box4VatReclaimedOnPurchases { get; set; }

    /// <summary>Box 5 — Net VAT to pay to HMRC or reclaim (Box 3 - Box 4)</summary>
    public decimal Box5NetVatPayable => Box3TotalVatDue - Box4VatReclaimedOnPurchases;

    /// <summary>Box 6 — Total value of sales and outputs excluding VAT (net turnover)</summary>
    public decimal Box6TotalSalesExcludingVat { get; set; }

    /// <summary>Box 7 — Total value of purchases and expenses excluding VAT</summary>
    public decimal Box7TotalPurchasesExcludingVat { get; set; }

    /// <summary>Box 8 — Total supplies to EC member states (post-Brexit = 0 for most)</summary>
    public decimal Box8SuppliesEc { get; set; } = 0m;

    /// <summary>Box 9 — Total acquisitions from EC member states (post-Brexit = 0 for most)</summary>
    public decimal Box9AcquisitionsEc { get; set; } = 0m;

    // ── Validation & Audit ────────────────────────────────────────────────────
    public bool IsValid { get; set; }
    public List<string> ValidationWarnings { get; set; } = new();
    public MtdVatBreakdown Breakdown { get; set; } = new();
    public int InvoiceCount { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}

public class MtdVatBreakdown
{
    public List<MtdVatLineItem> StandardRateItems { get; set; } = new();
    public List<MtdVatLineItem> ReducedRateItems { get; set; } = new();
    public List<MtdVatLineItem> ZeroRateItems { get; set; } = new();
    public List<MtdVatLineItem> ExemptItems { get; set; } = new();
}

public class MtdVatLineItem
{
    public string Description { get; set; } = string.Empty;
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TaxRate { get; set; }
    public int InvoiceCount { get; set; }
}

public class MtdVatReturnRequest
{
    [Required]
    public DateTime PeriodStart { get; set; }

    [Required]
    public DateTime PeriodEnd { get; set; }

    public string VatScheme { get; set; } = "Standard";

    [RegularExpression(@"^GB[0-9]{9}$", ErrorMessage = "VAT number must be in format GB123456789")]
    public string? VatRegistrationNumber { get; set; }

    // Box 6 — sales data not held in the system (manual entry from sales ledger)
    public decimal ManualBox6SalesExcludingVat { get; set; } = 0m;

    // Box 1 — output VAT on sales (manual, since we track purchase invoices, not sales)
    public decimal ManualBox1OutputVat { get; set; } = 0m;
}

// ══════════════════════════════════════════════════════════════════════════════
// Enhanced Spend Analytics — Drill-downs & Trends
// ══════════════════════════════════════════════════════════════════════════════

public class VendorDrilldownResponse
{
    public Guid? VendorId { get; set; }
    public string VendorName { get; set; } = string.Empty;
    public string? VendorEmail { get; set; }
    public string? VatNumber { get; set; }

    public int TotalInvoices { get; set; }
    public decimal TotalSpend { get; set; }
    public decimal TotalTax { get; set; }
    public decimal AverageInvoiceValue { get; set; }
    public decimal LargestInvoice { get; set; }
    public decimal SmallestInvoice { get; set; }

    public DateTime? FirstInvoiceDate { get; set; }
    public DateTime? LastInvoiceDate { get; set; }

    public List<SpendByMonth> MonthlyTrend { get; set; } = new();
    public List<InvoiceListResponse> RecentInvoices { get; set; } = new();
}

public class CategoryDrilldownResponse
{
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;

    public int TotalInvoices { get; set; }
    public decimal TotalSpend { get; set; }
    public decimal TotalTax { get; set; }
    public decimal PercentageOfTotalSpend { get; set; }
    public decimal AverageInvoiceValue { get; set; }

    public List<SpendByVendor> TopVendors { get; set; } = new();
    public List<SpendByMonth> MonthlyTrend { get; set; } = new();
}

public class SpendTrendRequest
{
    [Required]
    public DateTime From { get; set; }

    [Required]
    public DateTime To { get; set; }

    public string Granularity { get; set; } = "Monthly";  // Daily | Weekly | Monthly | Quarterly
    public string? Currency { get; set; }
    public Guid? VendorId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool CompareWithPreviousPeriod { get; set; } = false;
}

public class SpendTrendResponse
{
    public string Granularity { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal TotalSpend { get; set; }
    public int TotalInvoices { get; set; }
    public decimal AveragePeriodSpend { get; set; }
    public decimal GrowthVsPreviousPeriod { get; set; }
    public List<TrendDataPoint> DataPoints { get; set; } = new();
    public List<TrendDataPoint>? PreviousPeriodDataPoints { get; set; }
}

public class TrendDataPoint
{
    public string Label { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal Amount { get; set; }
    public decimal TaxAmount { get; set; }
    public int InvoiceCount { get; set; }
    public decimal? ChangeFromPrevious { get; set; }
    public decimal? ChangePercent { get; set; }
}

public class TopVendorsRequest
{
    public int Top { get; set; } = 10;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Currency { get; set; }
}

public class BudgetVarianceResponse
{
    public string Period { get; set; } = string.Empty;
    public decimal ActualSpend { get; set; }
    public decimal BudgetedSpend { get; set; }
    public decimal Variance { get; set; }
    public decimal VariancePercent { get; set; }
    public bool IsOverBudget { get; set; }
    public List<CategoryBudgetLine> ByCategory { get; set; } = new();
}

public class CategoryBudgetLine
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Actual { get; set; }
    public decimal Budget { get; set; }
    public decimal Variance { get; set; }
}
