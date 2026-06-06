using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/expense-categories")]
public class ExpenseCategoryController : ControllerBase
{
    private readonly IExpenseCategoryService _service;
    private readonly CurrentUserProvider _currentUser;

    public ExpenseCategoryController(IExpenseCategoryService service, CurrentUserProvider currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreateExpenseCategoryRequest request)
    {
        var result = await _service.CreateAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId)
    {
        var result = await _service.GetByCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpDelete("{categoryId:guid}")]
    public async Task<IActionResult> Delete(Guid companyId, Guid categoryId)
    {
        var result = await _service.DeleteAsync(categoryId, _currentUser.GetUserId());
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
