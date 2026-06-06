using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/companies")]
public class CompanyController : ControllerBase
{
    private readonly ICompanyService _companyService;
    private readonly CurrentUserProvider _currentUser;

    public CompanyController(ICompanyService companyService, CurrentUserProvider currentUser)
    {
        _companyService = companyService;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompanyRequest request)
    {
        var userId = _currentUser.GetUserId();
        var result = await _companyService.CreateCompanyAsync(request, userId);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyCompanies()
    {
        var userId = _currentUser.GetUserId();
        var result = await _companyService.GetMyCompaniesAsync(userId);
        return Ok(result.Data);
    }

    [HttpGet("{companyId:guid}")]
    public async Task<IActionResult> GetById(Guid companyId)
    {
        var userId = _currentUser.GetUserId();
        var result = await _companyService.GetCompanyByIdAsync(companyId, userId);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
