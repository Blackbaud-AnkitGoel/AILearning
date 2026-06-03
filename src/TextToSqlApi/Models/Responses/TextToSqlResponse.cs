namespace TextToSqlApi.Models.Responses;

/// <summary>
/// Encapsulates the result of a Text-to-SQL translation request.
/// </summary>
public sealed class TextToSqlResponse
{
    /// <summary>
    /// The SQL statement generated from the natural-language input.
    /// </summary>
    public string GeneratedSql { get; init; } = string.Empty;

    /// <summary>
    /// The original natural-language query echoed back for traceability.
    /// </summary>
    public string OriginalQuery { get; init; } = string.Empty;

    /// <summary>
    /// Confidence score between 0 and 1 indicating how confident the model is in the output.
    /// </summary>
    public double ConfidenceScore { get; init; }

    /// <summary>
    /// SQL dialect that was used to generate the statement.
    /// </summary>
    public string SqlDialect { get; init; } = string.Empty;

    /// <summary>
    /// Total tokens consumed by the underlying AI model call.
    /// </summary>
    public int TotalTokensUsed { get; init; }

    /// <summary>
    /// Unique identifier for this specific translation result, useful for audit logs.
    /// </summary>
    public string ResultId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// UTC timestamp when the result was produced.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Correlation ID echoed back from the request for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; init; }
}
