using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OcrInvoiceSaaS.Data;

namespace OcrInvoiceSaaS.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public HealthController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var dbReachable = false;
        string? dbError = null;

        try
        {
            dbReachable = await _db.Database.CanConnectAsync();
        }
        catch (Exception ex)
        {
            dbError = ex.Message;
        }

        var status = dbReachable ? "Healthy" : "Degraded";
        var code = dbReachable ? 200 : 503;

        return StatusCode(code, new
        {
            status,
            timestamp = DateTime.UtcNow,
            version = "1.0.0",
            database = new
            {
                reachable = dbReachable,
                error = dbError
            }
        });
    }
}
