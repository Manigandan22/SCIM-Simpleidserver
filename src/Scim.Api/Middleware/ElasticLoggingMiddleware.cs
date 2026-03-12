using System.Diagnostics;
using Serilog.Context;

namespace Scim.Api.Middleware;

/// <summary>
/// Logs structured HTTP request/response data to Elasticsearch via Serilog.
/// Health-check endpoints are skipped to avoid noise.
/// Request/response bodies are intentionally excluded to keep log volume low.
/// </summary>
public class ElasticLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ElasticLoggingMiddleware> _logger;

    // Paths excluded from logging to avoid noisy health-probe traffic
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health/live",
        "/health/ready"
    };

    public ElasticLoggingMiddleware(RequestDelegate next, ILogger<ElasticLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (ExcludedPaths.Contains(path))
        {
            await _next(context);
            return;
        }

        var method      = context.Request.Method;
        var query       = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null;
        var clientIp    = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        // TraceIdentifier maps to X-Request-Id / Activity trace id
        var correlationId = context.TraceIdentifier;

        // Push shared properties into every log event emitted during this request
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("ClientIp", clientIp))
        using (LogContext.PushProperty("RequestMethod", method))
        using (LogContext.PushProperty("RequestPath", path))
        {
            var sw = Stopwatch.StartNew();

            try
            {
                await _next(context);

                sw.Stop();

                var statusCode = context.Response.StatusCode;

                // 5xx → Error, 4xx → Warning, rest → Information
                if (statusCode >= 500)
                {
                    _logger.LogError(
                        "HTTP {Method} {Path}{Query} -> {StatusCode} [{ElapsedMs}ms]",
                        method, path, query, statusCode, sw.ElapsedMilliseconds);
                }
                else if (statusCode >= 400)
                {
                    _logger.LogWarning(
                        "HTTP {Method} {Path}{Query} -> {StatusCode} [{ElapsedMs}ms]",
                        method, path, query, statusCode, sw.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation(
                        "HTTP {Method} {Path}{Query} -> {StatusCode} [{ElapsedMs}ms]",
                        method, path, query, statusCode, sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();

                _logger.LogError(ex,
                    "HTTP {Method} {Path} threw unhandled exception after {ElapsedMs}ms",
                    method, path, sw.ElapsedMilliseconds);

                throw;
            }
        }
    }
}
