using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/vendors")]
public class VendorController : ControllerBase
{
    private readonly IVendorService _vendorService;
    private readonly CurrentUserProvider _currentUser;

    public VendorController(IVendorService vendorService, CurrentUserProvider currentUser)
    {
        _vendorService = vendorService;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreateVendorRequest request)
    {
        var userId = _currentUser.GetUserId();
        var result = await _vendorService.CreateVendorAsync(companyId, request, userId);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId)
    {
        var userId = _currentUser.GetUserId();
        var result = await _vendorService.GetVendorsByCompanyAsync(companyId, userId);
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
