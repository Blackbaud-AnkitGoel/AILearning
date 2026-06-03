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
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class TextToSqlController : ControllerBase
{
    private readonly ITextToSqlService  _textToSqlService;
    private readonly IRequestValidator  _validator;
    private readonly ILogger<TextToSqlController> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="TextToSqlController"/>.
    /// </summary>
    /// <param name="textToSqlService">The translation service.</param>
    /// <param name="validator">The request validator.</param>
    /// <param name="logger">Structured logger.</param>
    public TextToSqlController(
        ITextToSqlService textToSqlService,
        IRequestValidator validator,
        ILogger<TextToSqlController> logger)
    {
        _textToSqlService = textToSqlService ?? throw new ArgumentNullException(nameof(textToSqlService));
        _validator        = validator        ?? throw new ArgumentNullException(nameof(validator));
        _logger           = logger           ?? throw new ArgumentNullException(nameof(logger));
    }

    // -------------------------------------------------------------------------
    // POST api/v1/texttosql/translate
    // -------------------------------------------------------------------------

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
    [ProducesResponseType(typeof(TextToSqlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse),     StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse),     StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TranslateAsync(
        [FromBody] TextToSqlRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = request.CorrelationId ?? HttpContext.TraceIdentifier;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["Endpoint"]      = nameof(TranslateAsync)
        });

        _logger.LogInformation("Received translation request");

        // --- Validate ---
        var validationErrors = _validator.Validate(request);
        if (validationErrors.Count > 0)
        {
            _logger.LogWarning("Validation failed with {ErrorCount} error(s)", validationErrors.Count);

            return BadRequest(new ErrorResponse
            {
                ErrorCode     = "VALIDATION_FAILED",
                Message       = "One or more validation errors occurred.",
                Detail        = string.Join(" | ", validationErrors),
                CorrelationId = correlationId
            });
        }

        // --- Translate ---
        var response = await _textToSqlService
            .TranslateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Translation completed successfully. ResultId={ResultId}", response.ResultId);

        return Ok(response);
    }

    // -------------------------------------------------------------------------
    // GET api/v1/texttosql/health
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lightweight health-check endpoint confirming the API is reachable.
    /// </summary>
    /// <returns>200 OK with a simple status object.</returns>
    /// <response code="200">Service is healthy.</response>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health()
        => Ok(new { Status = "Healthy", Timestamp = DateTimeOffset.UtcNow });
}
