using AutoMapper;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Models;
using OcrInvoiceSaaS.Results;

namespace OcrInvoiceSaaS.Services;

public class ExpenseCategoryService : IExpenseCategoryService
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public ExpenseCategoryService(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }

    private async Task<bool> UserBelongsToCompanyAsync(Guid companyId, Guid userId)
        => await _db.CompanyUsers.AnyAsync(cu => cu.CompanyId == companyId && cu.UserId == userId);

    public async Task<ServiceResult<ExpenseCategoryResponse>> CreateAsync(
        Guid companyId, CreateExpenseCategoryRequest request, Guid userId)
    {
        if (!await UserBelongsToCompanyAsync(companyId, userId))
            return ServiceResult<ExpenseCategoryResponse>.Fail("Access denied.", 403);

        var category = new ExpenseCategory
        {
            CompanyId = companyId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim()
        };

        _db.ExpenseCategories.Add(category);
        await _db.SaveChangesAsync();
        return ServiceResult<ExpenseCategoryResponse>.Success(_mapper.Map<ExpenseCategoryResponse>(category), 201);
    }

    public async Task<ServiceResult<List<ExpenseCategoryResponse>>> GetByCompanyAsync(Guid companyId, Guid userId)
    {
        if (!await UserBelongsToCompanyAsync(companyId, userId))
            return ServiceResult<List<ExpenseCategoryResponse>>.Fail("Access denied.", 403);

        var categories = await _db.ExpenseCategories
            .Where(ec => ec.CompanyId == companyId && ec.IsActive)
            .OrderBy(ec => ec.Name)
            .ToListAsync();
        return ServiceResult<List<ExpenseCategoryResponse>>.Success(_mapper.Map<List<ExpenseCategoryResponse>>(categories));
    }

    public async Task<ServiceResult> DeleteAsync(Guid categoryId, Guid userId)
    {
        var category = await _db.ExpenseCategories.FindAsync(categoryId);
        if (category == null) return ServiceResult.Fail("Category not found.", 404);
        if (!await UserBelongsToCompanyAsync(category.CompanyId, userId)) return ServiceResult.Fail("Access denied.", 403);

        category.IsActive = false;
        await _db.SaveChangesAsync();
        return ServiceResult.Success(204);
    }
}
