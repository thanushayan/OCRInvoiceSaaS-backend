namespace OcrInvoiceSaaS.DTOs;

public class DashboardResponse
{
    public int TotalInvoices { get; set; }
    public decimal TotalAmount { get; set; }
    public int PendingReview { get; set; }
    public int Approved { get; set; }
    public int Failed { get; set; }
    public List<MonthlyTotal> MonthlyTotals { get; set; } = new();
    public List<InvoiceListResponse> RecentInvoices { get; set; } = new();
}

public class MonthlyTotal
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Total { get; set; }
    public int Count { get; set; }
}
