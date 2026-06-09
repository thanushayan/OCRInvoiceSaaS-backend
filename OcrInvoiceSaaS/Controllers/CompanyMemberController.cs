using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/members")]
public class CompanyMemberController : ControllerBase
{
    private readonly ICompanyMemberService _service;
    private readonly CurrentUserProvider _currentUser;

    public CompanyMemberController(ICompanyMemberService service, CurrentUserProvider currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    /// <summary>List all members of a company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId)
    {
        var result = await _service.GetMembersAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Add a registered user to a company by email address.</summary>
    [HttpPost]
    public async Task<IActionResult> Add(Guid companyId, [FromBody] InviteMemberRequest request)
    {
        var result = await _service.AddMemberAsync(companyId, request, _currentUser.GetUserId());
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Remove a member from a company.</summary>
    [HttpDelete("{targetUserId:guid}")]
    public async Task<IActionResult> Remove(Guid companyId, Guid targetUserId)
    {
        var result = await _service.RemoveMemberAsync(companyId, targetUserId, _currentUser.GetUserId());
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.StatusCode, new { error = result.Error });
    }
}
