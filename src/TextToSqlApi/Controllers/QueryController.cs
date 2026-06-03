using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;
using TextToSqlApi.Models.Requests;
using TextToSqlApi.Models.Responses;

namespace TextToSqlApi.Controllers;

/// <summary>
/// Exposes the end-to-end Text-to-SQL query pipeline as a single POST endpoint.
/// </summary>
/// <remarks>
/// The pipeline executes the following steps in sequence:
/// <list type="number">
///   <item><description>Generate SQL from the natural-language question via <see cref="ITextToSqlService"/>.</description></item>
///   <item><description>Validate the generated SQL for safety via <see cref="ISqlValidator"/>.</description></item>
///   <item><description>Execute the validated SQL against SQL Server via <see cref="ISqlExecutionService"/>.</description></item>
///   <item><description>Summarise the results in business-friendly English via <see cref="IResultSummaryService"/>.</description></item>
///   <item><description>Return the full <see cref="QueryResponse"/> to the caller.</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/query")]
[Produces("application/json")]
public sealed class QueryController : ControllerBase
{
    private readonly ITextToSqlService _textToSqlService;
    private readonly ISqlValidator _sqlValidator;
    private readonly ISqlExecutionService _sqlExecutionService;
    private readonly IResultSummaryService _resultSummaryService;
    private readonly IDatabaseSchemaService _databaseSchemaService;
    private readonly ILogger<QueryController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initialises a new instance of <see cref="QueryController"/>.
    /// </summary>
    public QueryController(
        ITextToSqlService textToSqlService,
        ISqlValidator sqlValidator,
        ISqlExecutionService sqlExecutionService,
        IResultSummaryService resultSummaryService,
        IDatabaseSchemaService databaseSchemaService,
        ILogger<QueryController> logger)
    {
        ArgumentNullException.ThrowIfNull(textToSqlService);
        ArgumentNullException.ThrowIfNull(sqlValidator);
        ArgumentNullException.ThrowIfNull(sqlExecutionService);
        ArgumentNullException.ThrowIfNull(resultSummaryService);
        ArgumentNullException.ThrowIfNull(databaseSchemaService);
        ArgumentNullException.ThrowIfNull(logger);

        _textToSqlService = textToSqlService;
        _sqlValidator = sqlValidator;
        _sqlExecutionService = sqlExecutionService;
        _resultSummaryService = resultSummaryService;
        _databaseSchemaService = databaseSchemaService;
        _logger = logger;
    }

    /// <summary>
    /// Translates a natural-language question into SQL, executes it, and returns
    /// both the raw data and a business-friendly summary.
    /// </summary>
    /// <param name="request">The query request payload.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description>200 OK – Query completed successfully.</description></item>
    ///   <item><description>400 Bad Request – Validation errors or unsafe SQL generated.</description></item>
    ///   <item><description>500 Internal Server Error – Unexpected failure.</description></item>
    /// </list>
    /// </returns>
    /// <response code="200">Query completed. Returns question, SQL, summary, and data rows.</response>
    /// <response code="400">One or more validation errors prevented processing.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost]
    [ProducesResponseType(typeof(QueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> QueryAsync(
        [FromBody] QueryRequest request,
        CancellationToken cancellationToken)
    {
        string correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString()
            : request.CorrelationId;

        using var scope = _logger.BeginScope(
            new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        _logger.LogInformation(
            "Query pipeline started. Question={Question}, Dialect={Dialect}, CorrelationId={CorrelationId}",
            request.Question,
            request.SqlDialect ?? "T-SQL",
            correlationId);

        // ── Step 1: Generate SQL ──────────────────────────────────────────
        _logger.LogDebug("Step 1/4: Generating SQL from natural-language question.");

        // Fetch live schema from the database (cached for 30 min).
        string schemaContext = string.Empty;
        try
        {
            schemaContext = await _databaseSchemaService
                .GetSchemaContextAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Schema fetch failed; proceeding without schema context. CorrelationId={CorrelationId}",
                correlationId);
        }

        var translationRequest = new TextToSqlRequest
        {
            NaturalLanguageQuery = request.Question,
            SqlDialect = request.SqlDialect ?? "T-SQL",
            MaxRows = request.MaxRows ?? 100,
            SchemaContext = schemaContext,
            CorrelationId = correlationId
        };

        TextToSqlResponse translationResult;
        try
        {
            translationResult = await _textToSqlService
                .TranslateAsync(translationRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL generation failed. CorrelationId={CorrelationId}", correlationId);
            return Problem(
                detail: "The AI model failed to generate a SQL statement. Please try rephrasing your question.",
                title: "SQL Generation Failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        string generatedSql = translationResult.GeneratedSql;
        _logger.LogInformation(
            "Step 1/4 complete: SQL generated. CorrelationId={CorrelationId}, SQL={Sql}",
            correlationId,
            generatedSql);

        // ── Step 2: Validate SQL ──────────────────────────────────────────
        _logger.LogDebug("Step 2/4: Validating generated SQL for safety.");

        SqlValidationResult validationResult = _sqlValidator.Validate(generatedSql);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "Step 2/4: SQL validation failed. CorrelationId={CorrelationId}, Reason={Reason}, SQL={Sql}",
                correlationId,
                validationResult.ErrorMessage,
                generatedSql);

            return Problem(
                detail: validationResult.ErrorMessage,
                title: "Generated SQL Failed Safety Validation",
                statusCode: StatusCodes.Status400BadRequest);
        }

        _logger.LogInformation(
            "Step 2/4 complete: SQL passed validation. CorrelationId={CorrelationId}",
            correlationId);

        // Use normalised SQL (may include injected TOP 100) for execution.
        string safeSql = validationResult.NormalizedSql ?? generatedSql;

        // ── Step 3: Execute SQL ───────────────────────────────────────────
        _logger.LogDebug("Step 3/4: Executing validated SQL against the database.");

        IReadOnlyList<dynamic> rows;
        try
        {
            rows = await _sqlExecutionService
                .ExecuteAsync(safeSql, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "SQL execution timed out. CorrelationId={CorrelationId}", correlationId);
            return Problem(
                detail: "The query took too long to execute. Try a more specific question.",
                title: "Query Timeout",
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL execution failed. CorrelationId={CorrelationId}", correlationId);
            return Problem(
                detail: "An error occurred while executing the query against the database.",
                title: "SQL Execution Failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        _logger.LogInformation(
            "Step 3/4 complete: SQL executed. RowCount={RowCount}, CorrelationId={CorrelationId}",
            rows.Count,
            correlationId);

        // ── Step 4: Summarise results ─────────────────────────────────────
        _logger.LogDebug("Step 4/4: Generating business summary of results.");

        string resultsJson = JsonSerializer.Serialize(rows, JsonOptions);
        string summary;

        try
        {
            summary = await _resultSummaryService
                .SummarizeAsync(request.Question, resultsJson, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Summary generation failed; returning results without summary. CorrelationId={CorrelationId}",
                correlationId);

            summary = "Summary could not be generated at this time.";
        }

        _logger.LogInformation(
            "Query pipeline completed successfully. RowCount={RowCount}, CorrelationId={CorrelationId}",
            rows.Count,
            correlationId);

        var response = new QueryResponse
        {
            Question = request.Question,
            SqlQuery = safeSql,
            Summary = summary,
            Data = rows,
            RowCount = rows.Count,
            CorrelationId = correlationId,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        return Ok(response);
    }
}
