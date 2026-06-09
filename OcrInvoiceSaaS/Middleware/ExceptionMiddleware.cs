using System.Net;
using System.Text.Json;

namespace OcrInvoiceSaaS.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt.");
            await WriteErrorResponse(context, HttpStatusCode.Unauthorized, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found.");
            await WriteErrorResponse(context, HttpStatusCode.NotFound, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred.");
            await WriteErrorResponse(context, HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    private static async Task WriteErrorResponse(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new { error = message, statusCode = (int)statusCode };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
