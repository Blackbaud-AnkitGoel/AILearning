namespace TextToSqlApi.Interfaces;

/// <summary>
/// Defines the contract for the low-level AI SQL generation service.
/// Implementations are responsible for loading an external prompt template,
/// invoking the configured chat-completion model via Semantic Kernel, and
/// returning a clean, markdown-stripped SQL string.
/// </summary>
public interface ISqlGenerationService
{
    /// <summary>
    /// Generates a SQL query from a natural-language question using an AI model.
    /// </summary>
    /// <param name="question">
    /// The natural-language question to translate into SQL.
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="schemaContext">
    /// Optional DDL or JSON schema describing the target database.
    /// When provided, it is injected into the prompt to improve accuracy.
    /// </param>
    /// <param name="dialect">
    /// Target SQL dialect (e.g., <c>T-SQL</c>, <c>PostgreSQL</c>, <c>MySQL</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="maxRows">
    /// Maximum number of rows the generated query should return.
    /// Applied as <c>TOP n</c> (T-SQL) or <c>LIMIT n</c> (PostgreSQL/MySQL).
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation signals.</param>
    /// <returns>
    /// A clean SQL string with no markdown fences or prose.
    /// Returns <c>CANNOT_GENERATE_SQL</c> when the model cannot answer from the given schema.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="question"/> or <paramref name="dialect"/> is null or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the AI model invocation fails for a non-cancellation reason.
    /// </exception>
    Task<string> GenerateSqlAsync(
        string question,
        string? schemaContext,
        string dialect,
        int maxRows,
        CancellationToken cancellationToken = default);
}
