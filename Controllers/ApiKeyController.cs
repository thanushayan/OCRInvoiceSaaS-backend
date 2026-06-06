using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.DTOs;
using OcrInvoiceSaaS.Interfaces;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/companies/{companyId:guid}/api-keys")]
public class ApiKeyController : ControllerBase
{
    private readonly IApiKeyService _apiKeyService;
    private readonly CurrentUserProvider _currentUser;

    public ApiKeyController(IApiKeyService apiKeyService, CurrentUserProvider currentUser)
    {
        _apiKeyService = apiKeyService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Create a new API key for this company.
    ///
    /// The full key is returned ONCE in the response and never stored.
    /// The client must save it immediately — it cannot be retrieved again.
    ///
    /// Available scopes:
    ///   invoices:read, invoices:write, invoices:delete
    ///   vendors:read, vendors:write
    ///   dashboard:read
    ///   ocr:run
    ///   company:read
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid companyId, [FromBody] CreateApiKeyRequest request)
    {
        var result = await _apiKeyService.CreateAsync(_currentUser.GetUserId(), companyId, request);
        return result.IsSuccess
            ? StatusCode(result.StatusCode, result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// List all active API keys for this company.
    /// Only the key prefix (first 12 characters) is shown — never the full key.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid companyId)
    {
        var result = await _apiKeyService.GetByCompanyAsync(companyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>
    /// Revoke an API key immediately.
    /// Any in-flight requests using this key will be rejected after the next middleware check.
    /// </summary>
    [HttpDelete("{keyId:guid}")]
    public async Task<IActionResult> Revoke(Guid companyId, Guid keyId)
    {
        var result = await _apiKeyService.RevokeAsync(keyId, _currentUser.GetUserId());
        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.StatusCode, new { error = result.Error });
    }

    /// <summary>Returns the full list of valid scopes that can be assigned to an API key.</summary>
    [HttpGet("scopes")]
    public IActionResult GetScopes(Guid companyId)
    {
        return Ok(new
        {
            scopes = ApiKeyService.AllowedScopes.OrderBy(s => s).ToList(),
            description = new Dictionary<string, string>
            {
                ["invoices:read"]    = "Read invoices and line items",
                ["invoices:write"]   = "Create and update invoices",
                ["invoices:delete"]  = "Delete invoices",
                ["vendors:read"]     = "Read vendor records",
                ["vendors:write"]    = "Create and update vendors",
                ["dashboard:read"]   = "Access dashboard summary data",
                ["ocr:run"]          = "Trigger OCR processing on invoices",
                ["company:read"]     = "Read company profile and members"
            }
        });
    }
}
