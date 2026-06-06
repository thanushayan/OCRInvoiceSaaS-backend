using OcrInvoiceSaaS.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OcrInvoiceSaaS.Libs;
using OcrInvoiceSaaS.Services;

namespace OcrInvoiceSaaS.Controllers;

[Authorize]
[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly IAuditService _auditService;
    private readonly CurrentUserProvider _currentUser;

    public AuditController(IAuditService auditService, CurrentUserProvider currentUser)
    {
        _auditService = auditService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Retrieve the audit trail for any entity (Invoice, Company, Vendor, etc.).
    /// Example: GET /api/audit/Invoice/{invoiceId}
    /// </summary>
    [HttpGet("{entityType}/{entityId:guid}")]
    public async Task<IActionResult> GetLogs(string entityType, Guid entityId)
    {
        var userId = _currentUser.GetUserId();
        var logs = await _auditService.GetLogsAsync(entityType, entityId, userId);
        return Ok(logs);
    }
}
