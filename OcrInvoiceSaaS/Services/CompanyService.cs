using AutoMapper;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class CompanyService : ICompanyService
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public CompanyService(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<ServiceResult<CompanyResponse>> CreateCompanyAsync(CreateCompanyRequest request, Guid userId)
    {
        var company = new Company
        {
            Name = request.Name.Trim(),
            RegistrationNumber = request.RegistrationNumber?.Trim(),
            VatNumber = request.VatNumber?.Trim(),
            Address = request.Address?.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim().ToLower()
        };

        _db.Companies.Add(company);

        // Link the creating user as Owner
        _db.CompanyUsers.Add(new CompanyUser
        {
            CompanyId = company.Id,
            UserId = userId,
            Role = "Owner"
        });

        await _db.SaveChangesAsync();

        var response = _mapper.Map<CompanyResponse>(company);
        response.UserRole = "Owner";

        return ServiceResult<CompanyResponse>.Success(response, 201);
    }

    public async Task<ServiceResult<List<CompanyResponse>>> GetMyCompaniesAsync(Guid userId)
    {
        var companyUsers = await _db.CompanyUsers
            .Include(cu => cu.Company)
            .Where(cu => cu.UserId == userId && cu.Company.IsActive)
            .ToListAsync();

        var result = companyUsers.Select(cu =>
        {
            var r = _mapper.Map<CompanyResponse>(cu.Company);
            r.UserRole = cu.Role;
            return r;
        }).ToList();

        return ServiceResult<List<CompanyResponse>>.Success(result);
    }

    public async Task<ServiceResult<CompanyResponse>> GetCompanyByIdAsync(Guid companyId, Guid userId)
    {
        var cu = await _db.CompanyUsers
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == userId);

        if (cu == null)
            return ServiceResult<CompanyResponse>.Fail("Company not found or access denied.", 404);

        var response = _mapper.Map<CompanyResponse>(cu.Company);
        response.UserRole = cu.Role;

        return ServiceResult<CompanyResponse>.Success(response);
    }
}
