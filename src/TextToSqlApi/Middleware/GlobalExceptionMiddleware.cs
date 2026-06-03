using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace TextToSqlApi.Middleware;

/// <summary>
/// ASP.NET Core middleware that catches all unhandled exceptions and converts them
/// into RFC 7807 Problem Details JSON responses.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Security</strong>: Internal exception details (stack traces, inner
/// exception messages) are never exposed to the caller.  Only a generic error
/// message and a correlation ID are included in the response body, keeping
/// sensitive information server-side.
/// </para>
/// <para>
/// <strong>Correlation tracking</strong>: A correlation ID is extracted from the
/// <c>X-Correlation-Id</c> request header when present; otherwise a new GUID is
/// generated.  The same ID is echoed in the response header and body so that log
/// entries can be correlated with client-side reports.
/// </para>
/// <para>
/// Register this middleware as the <b>first</b> entry in the pipeline (before
/// routing, authentication, etc.) so that exceptions from any subsequent
/// middleware are also caught.
/// </para>
/// </remarks>
public sealed class GlobalExceptionMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Initialises a new instance of <see cref="GlobalExceptionMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    /// <param name="logger">Structured logger for this middleware.</param>
    /// <param name="environment">Provides the current hosting environment name.</param>
    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(environment);

        _next = next;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Invokes the middleware, forwarding the request to the next component in the
    /// pipeline and handling any unhandled exceptions that propagate back.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Resolve or generate the correlation ID early so it is available in logs
        // even if the exception occurs deep in the pipeline.
        string correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out var existing)
            && !string.IsNullOrWhiteSpace(existing)
                ? existing.ToString()
                : Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — log at a low level and return 499-equivalent.
            _logger.LogInformation(
                "Request cancelled by client. CorrelationId={CorrelationId}, Path={Path}",
                correlationId,
                context.Request.Path);

            // 499 is not an official status code; use 400 with a clear title instead.
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception. CorrelationId={CorrelationId}, Method={Method}, Path={Path}",
                correlationId,
                context.Request.Method,
                context.Request.Path);

            await WriteProblemsDetailsAsync(context, ex, correlationId).ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task WriteProblemsDetailsAsync(
        HttpContext context,
        Exception exception,
        string correlationId)
    {
        if (context.Response.HasStarted)
        {
            // Headers already sent — nothing we can do.
            _logger.LogWarning(
                "Response has already started; cannot write ProblemDetails. CorrelationId={CorrelationId}",
                correlationId);
            return;
        }

        int statusCode = MapExceptionToStatusCode(exception);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = MapExceptionToTitle(exception),
            Detail = _environment.IsProduction()
                ? "An unexpected error occurred. Please contact support and quote the correlation ID."
                : exception.Message,
            Instance = context.Request.Path,
            Extensions =
            {
                ["correlationId"] = correlationId,
                ["occurredAt"] = DateTimeOffset.UtcNow
            }
        };

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(problem, JsonOptions))
            .ConfigureAwait(false);
    }

    private static int MapExceptionToStatusCode(Exception exception) => exception switch
    {
        ArgumentException or ArgumentNullException => StatusCodes.Status400BadRequest,
        UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
        TimeoutException => StatusCodes.Status504GatewayTimeout,
        NotImplementedException => StatusCodes.Status501NotImplemented,
        InvalidOperationException => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string MapExceptionToTitle(Exception exception) => exception switch
    {
        ArgumentException or ArgumentNullException => "Bad Request",
        UnauthorizedAccessException => "Unauthorised",
        TimeoutException => "Gateway Timeout",
        NotImplementedException => "Not Implemented",
        InvalidOperationException => "Unprocessable Request",
        _ => "Internal Server Error"
    };
}
