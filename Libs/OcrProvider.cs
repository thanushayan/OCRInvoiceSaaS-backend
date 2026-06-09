using System.Security.Cryptography;
using System.Text;

namespace OcrInvoiceSaaS.Libs;

/// <summary>
/// Fields extracted from an invoice document by an OCR provider.
/// </summary>
public class OcrExtractionResult
{
    public string RawText { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? VendorName { get; set; }
}

/// <summary>
/// Abstraction over OCR backends (GCP Vision, AWS Textract, …).
/// Swap implementations via DI in Program.cs.
/// </summary>
public interface IOcrProvider
{
    string ProviderName { get; }
    Task<OcrExtractionResult> ExtractAsync(string fileUrl, string fileType);
}

/// <summary>
/// Deterministic fake OCR provider for development and testing.
/// Derives stable pseudo-random values from the file URL so repeated runs
/// of the same document extract the same data.
/// </summary>
public class MockOcrProvider : IOcrProvider
{
    public string ProviderName => "MockOcr";

    private static readonly string[] VendorNames =
    [
        "Acme Supplies Ltd", "Northwind Traders", "Globex Industries",
        "Initech Solutions", "Stark Components", "Wayne Logistics"
    ];

    public async Task<OcrExtractionResult> ExtractAsync(string fileUrl, string fileType)
    {
        // Simulate provider latency
        await Task.Delay(Random.Shared.Next(50, 200));

        var seedBytes = MD5.HashData(Encoding.UTF8.GetBytes(fileUrl));
        var seed = BitConverter.ToInt32(seedBytes, 0);
        var rng = new Random(seed);

        var invoiceNumber = $"INV-{rng.Next(1000, 99999)}";
        var invoiceDate = DateTime.UtcNow.Date.AddDays(-rng.Next(0, 60));
        var totalAmount = Math.Round((decimal)(rng.NextDouble() * 4900 + 100), 2);
        var vendorName = VendorNames[Math.Abs(seed) % VendorNames.Length];

        return new OcrExtractionResult
        {
            InvoiceNumber = invoiceNumber,
            InvoiceDate = invoiceDate,
            TotalAmount = totalAmount,
            VendorName = vendorName,
            RawText =
                $"INVOICE\n{vendorName}\nInvoice No: {invoiceNumber}\n" +
                $"Date: {invoiceDate:yyyy-MM-dd}\nTotal Due: {totalAmount:N2} GBP\n" +
                $"Source: {fileUrl} ({fileType})"
        };
    }
}
