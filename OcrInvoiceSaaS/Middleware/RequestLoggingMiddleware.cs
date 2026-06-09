using System.Diagnostics;

namespace OcrInvoiceSaaS.Middleware;

/// <summary>
/// Logs every HTTP request with method, path, status code, and duration.
/// Skips /health and Swagger endpoints to reduce noise.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    private static readonly string[] SkippedPaths =
        ["/health", "/swagger", "/favicon.ico"];

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (SkippedPaths.Any(s => path.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();

        await _next(context);

        sw.Stop();

        var level = context.Response.StatusCode >= 500
            ? LogLevel.Error
            : context.Response.StatusCode >= 400
                ? LogLevel.Warning
                : LogLevel.Information;

        _logger.Log(level,
            "{Method} {Path} → {StatusCode} ({Duration}ms)",
            context.Request.Method,
            path,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds);
    }
}
