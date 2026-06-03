namespace TextToSqlApi.Interfaces;

/// <summary>
/// Defines the contract for generating a business-friendly English summary
/// of SQL query results using an AI language model.
/// </summary>
public interface IResultSummaryService
{
    /// <summary>
    /// Produces a concise, non-technical summary of the provided SQL results
    /// in the context of the original user question.
    /// </summary>
    /// <param name="question">The original natural-language question asked by the user.</param>
    /// <param name="resultsJson">
    /// The SQL execution results serialised as a JSON string.
    /// May be an empty JSON array (<c>"[]"</c>) when the query returned no rows.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation signals.</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that resolves to a plain-English summary string
    /// suitable for display to a business user.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="question"/> or <paramref name="resultsJson"/> is null or whitespace.
    /// </exception>
    Task<string> SummarizeAsync(
        string question,
        string resultsJson,
        CancellationToken cancellationToken = default);
}
