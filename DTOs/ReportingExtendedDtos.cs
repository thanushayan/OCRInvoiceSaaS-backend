using System.ComponentModel.DataAnnotations;

namespace OcrInvoiceSaaS.DTOs;

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

    public string VatScheme { get; set; } = "Standard";
    public string? CompanyVatNumber { get; set; }
}

public class MtdVatReturnResponse
{
    public Guid CompanyId { get; set; }
    public string? VatRegistrationNumber { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public string VatScheme { get; set; } = "Standard";
    public DateTime CalculatedAt { get; set; }
    public decimal Box1VatDueOnSales { get; set; }
    public decimal Box2VatDueOnAcquisitions { get; set; } = 0m;
    public decimal Box3TotalVatDue => Box1VatDueOnSales + Box2VatDueOnAcquisitions;
    public decimal Box4VatReclaimedOnPurchases { get; set; }
    public decimal Box5NetVatPayable => Box3TotalVatDue - Box4VatReclaimedOnPurchases;
    public decimal Box6TotalSalesExcludingVat { get; set; }
    public decimal Box7TotalPurchasesExcludingVat { get; set; }
    public decimal Box8SuppliesEc { get; set; } = 0m;
    public decimal Box9AcquisitionsEc { get; set; } = 0m;
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

    public decimal ManualBox6SalesExcludingVat { get; set; } = 0m;
    public decimal ManualBox1OutputVat { get; set; } = 0m;
}

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

    public string Granularity { get; set; } = "Monthly";
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
