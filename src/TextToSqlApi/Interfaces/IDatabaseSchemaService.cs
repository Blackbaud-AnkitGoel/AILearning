namespace TextToSqlApi.Interfaces;

/// <summary>
/// Defines the contract for reading SQL Server schema metadata and generating
/// a textual schema description suitable for injection into AI prompts.
/// </summary>
public interface IDatabaseSchemaService
{
    /// <summary>
    /// Returns a formatted string that describes the database schema — tables,
    /// columns, types, and foreign-key relationships — suitable for embedding
    /// in a Semantic Kernel prompt to improve SQL generation accuracy.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation signals.</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that resolves to a multi-line schema
    /// description string.  The string may be empty when the target database
    /// contains no user tables.
    /// </returns>
    Task<string> GetSchemaContextAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the in-memory schema cache, forcing the next call to
    /// <see cref="GetSchemaContextAsync"/> to re-read from the database.
    /// </summary>
    void InvalidateCache();
}
