using OcrInvoiceSaaS.Models;

namespace OcrInvoiceSaaS.Libs;

public static class PaginationHelper
{
    /// <summary>Applies skip/take to any ordered IQueryable.</summary>
    public static IQueryable<T> Paginate<T>(this IQueryable<T> query, int page, int pageSize)
    {
        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        return query.Skip((safePage - 1) * safeSize).Take(safeSize);
    }
}

public static class InvoiceStatusHelper
{
    private static readonly InvoiceStatus[] ForwardFlow =
    [
        InvoiceStatus.Uploaded,
        InvoiceStatus.Processing,
        InvoiceStatus.Processed,
        InvoiceStatus.Reviewed,
        InvoiceStatus.Approved
    ];

    /// <summary>
    /// Returns true only if transitioning from current → next is a valid forward move
    /// or a reset to Failed. Prevents analysts from accidentally going backwards.
    /// </summary>
    public static bool IsValidTransition(InvoiceStatus current, InvoiceStatus next)
    {
        if (next == InvoiceStatus.Failed) return true;

        int currentIndex = Array.IndexOf(ForwardFlow, current);
        int nextIndex = Array.IndexOf(ForwardFlow, next);

        return nextIndex > currentIndex;
    }

    public static string FriendlyStatus(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Uploaded => "Awaiting OCR",
        InvoiceStatus.Processing => "OCR in Progress",
        InvoiceStatus.Processed => "Awaiting Review",
        InvoiceStatus.Reviewed => "Awaiting Approval",
        InvoiceStatus.Approved => "Approved",
        InvoiceStatus.Failed => "Processing Failed",
        _ => status.ToString()
    };
}
