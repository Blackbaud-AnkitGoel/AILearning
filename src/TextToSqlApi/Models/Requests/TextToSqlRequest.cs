namespace TextToSqlApi.Models.Requests;

/// <summary>
/// Represents an incoming natural-language query request to be converted into SQL.
/// </summary>
public sealed class TextToSqlRequest
{
    /// <summary>
    /// The natural-language question the user wants to translate to SQL.
    /// </summary>
    /// <example>Show me all orders placed in the last 30 days.</example>
    public string NaturalLanguageQuery { get; init; } = string.Empty;

    /// <summary>
    /// Optional target database schema context (e.g., table definitions as DDL or JSON).
    /// When provided, it is injected into the prompt to improve SQL accuracy.
    /// </summary>
    public string? SchemaContext { get; init; }

    /// <summary>
    /// The SQL dialect to target (e.g., T-SQL, PostgreSQL, MySQL).
    /// Defaults to T-SQL when not specified.
    /// </summary>
    public string SqlDialect { get; init; } = "T-SQL";

    /// <summary>
    /// Maximum number of rows the generated SQL should return (applied as TOP/LIMIT).
    /// Defaults to 100.
    /// </summary>
    public int MaxRows { get; init; } = 100;

    /// <summary>
    /// Correlation ID supplied by the caller for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; init; }
}
