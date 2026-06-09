using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Interfaces;

public interface IAuthService
{
    Task<ServiceResult<AuthResponse>> RegisterAsync(RegisterRequest request);
    Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request);
    Task<ServiceResult<UserProfileResponse>> GetCurrentUserAsync(Guid userId);
}

public interface ICompanyService
{
    Task<ServiceResult<CompanyResponse>> CreateCompanyAsync(CreateCompanyRequest request, Guid userId);
    Task<ServiceResult<List<CompanyResponse>>> GetMyCompaniesAsync(Guid userId);
    Task<ServiceResult<CompanyResponse>> GetCompanyByIdAsync(Guid companyId, Guid userId);
}

public interface IVendorService
{
    Task<ServiceResult<VendorResponse>> CreateVendorAsync(Guid companyId, CreateVendorRequest request, Guid userId);
    Task<ServiceResult<List<VendorResponse>>> GetVendorsByCompanyAsync(Guid companyId, Guid userId);
}

public interface IInvoiceService
{
    Task<ServiceResult<InvoiceResponse>> CreateInvoiceAsync(Guid companyId, CreateInvoiceRequest request, Guid userId);
    Task<ServiceResult<List<InvoiceListResponse>>> GetInvoicesAsync(Guid companyId, Guid userId);
    Task<ServiceResult<InvoiceResponse>> GetInvoiceByIdAsync(Guid invoiceId, Guid userId);
    Task<ServiceResult<InvoiceResponse>> UpdateInvoiceAsync(Guid invoiceId, UpdateInvoiceRequest request, Guid userId);
}

public interface IOcrService
{
    Task<ServiceResult<OcrResultResponse>> ProcessInvoiceAsync(Guid invoiceId, Guid userId);
}

public interface IDashboardService
{
    Task<ServiceResult<DashboardResponse>> GetDashboardAsync(Guid companyId, Guid userId);
}
