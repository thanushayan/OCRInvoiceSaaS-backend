using AutoMapper;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class VendorService : IVendorService
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public VendorService(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }

    private async Task<bool> UserBelongsToCompanyAsync(Guid companyId, Guid userId)
        => await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);

    public async Task<ServiceResult<VendorResponse>> CreateVendorAsync(Guid companyId, CreateVendorRequest request, Guid userId)
    {
        if (!await UserBelongsToCompanyAsync(companyId, userId))
            return ServiceResult<VendorResponse>.Fail("Access denied.", 403);

        var vendor = new Vendor
        {
            CompanyId = companyId, Name = request.Name.Trim(),
            ContactEmail = request.ContactEmail?.Trim().ToLower(),
            Phone = request.Phone?.Trim(), Address = request.Address?.Trim(), VatNumber = request.VatNumber?.Trim()
        };

        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        return ServiceResult<VendorResponse>.Success(_mapper.Map<VendorResponse>(vendor), 201);
    }

    public async Task<ServiceResult<List<VendorResponse>>> GetVendorsByCompanyAsync(Guid companyId, Guid userId)
    {
        if (!await UserBelongsToCompanyAsync(companyId, userId))
            return ServiceResult<List<VendorResponse>>.Fail("Access denied.", 403);

        var vendors = await _db.Vendors.Where(v => v.CompanyId == companyId && v.IsActive).OrderBy(v => v.Name).ToListAsync();
        return ServiceResult<List<VendorResponse>>.Success(_mapper.Map<List<VendorResponse>>(vendors));
    }
}
