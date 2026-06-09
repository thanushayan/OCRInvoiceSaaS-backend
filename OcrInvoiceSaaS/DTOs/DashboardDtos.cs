namespace OcrInvoiceSaaS.DTOs;

public class DashboardResponse
{
    public int TotalInvoices { get; set; }
    public int ProcessedInvoices { get; set; }
    public int PendingInvoices { get; set; }
    public int ApprovedInvoices { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "GBP";
    public List<MonthlyInvoiceSummary> MonthlySummary { get; set; } = new();
    public List<InvoiceListResponse> RecentInvoices { get; set; } = new();
}

public class MonthlyInvoiceSummary
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal TotalAmount { get; set; }
}
