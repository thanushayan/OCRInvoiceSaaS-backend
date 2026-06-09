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

    /// <summary>Create a new company. The calling user becomes the Owner.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompanyRequest request)
    {
        var userId = _currentUser.GetUserId();
        var result = await _companyService.CreateCompanyAsync(request, userId);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>List all companies the current user belongs to.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMyCompanies()
    {
        var userId = _currentUser.GetUserId();
        var result = await _companyService.GetMyCompaniesAsync(userId);
        return Ok(result.Data);
    }

    /// <summary>Get a single company by ID (must be a member).</summary>
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
