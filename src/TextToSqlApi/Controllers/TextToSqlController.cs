using Microsoft.AspNetCore.Mvc;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models.Requests;
using TextToSqlApi.Models.Responses;

namespace TextToSqlApi.Controllers;

/// <summary>
/// Exposes endpoints for translating natural-language questions into SQL statements
/// using an Azure OpenAI–backed Semantic Kernel pipeline.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class TextToSqlController : ControllerBase
{
    private readonly ITextToSqlService _textToSqlService;
    private readonly IRequestValidator _validator;
    private readonly ILogger<TextToSqlController> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="TextToSqlController"/>.
    /// </summary>
    public TextToSqlController(
        ITextToSqlService textToSqlService,
        IRequestValidator validator,
        ILogger<TextToSqlController> logger)
    {
        _textToSqlService = textToSqlService;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Translates a natural-language query into a SQL statement.
    /// </summary>
    /// <param name="request">The translation request payload.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description>200 OK – Translation succeeded.</description></item>
    ///   <item><description>400 Bad Request – Validation errors.</description></item>
    ///   <item><description>500 Internal Server Error – Unexpected failure.</description></item>
    /// </list>
    /// </returns>
    /// <response code="200">Returns the generated SQL and metadata.</response>
    /// <response code="400">One or more validation errors prevented processing.</response>
    /// <response code="500">An unexpected error occurred during AI model invocation.</response>
    [HttpPost("translate")]
    [ProducesResponseType<TextToSqlResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TranslateAsync(
        [FromBody] TextToSqlRequest request,
        CancellationToken cancellationToken)
    {
        string correlationId = request.CorrelationId
            ?? HttpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        using var _ = _logger.BeginScope(
            new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        IReadOnlyList<string> errors = _validator.Validate(request);
        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "Validation failed with {ErrorCount} error(s). CorrelationId={CorrelationId}",
                errors.Count, correlationId);
            return BadRequest(new ErrorResponse
            {
                ErrorCode = "VALIDATION_FAILED",
                Message = string.Join("; ", errors),
                CorrelationId = correlationId
            });
        }

        TextToSqlResponse response = await _textToSqlService
            .TranslateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return Ok(response);
    }

    /// <summary>
    /// Lightweight health-check endpoint confirming the API is reachable.
    /// </summary>
    /// <returns>200 OK with a simple status object.</returns>
    /// <response code="200">Service is healthy.</response>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health() =>
        Ok(new { Status = "Healthy", Timestamp = DateTimeOffset.UtcNow });
}
