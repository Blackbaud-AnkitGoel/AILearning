using TextToSqlApi.Models.Requests;
using TextToSqlApi.Models.Responses;

namespace TextToSqlApi.Interfaces;

/// <summary>
/// Defines the contract for the core Text-to-SQL translation service.
/// </summary>
public interface ITextToSqlService
{
    /// <summary>
    /// Translates a natural-language query into a SQL statement using an AI model.
    /// </summary>
    /// <param name="request">The translation request.</param>
    /// <param name="cancellationToken">Token to observe for cancellation signals.</param>
    /// <returns>A <see cref="TextToSqlResponse"/> containing the generated SQL and metadata.</returns>
    Task<TextToSqlResponse> TranslateAsync(
        TextToSqlRequest request,
        CancellationToken cancellationToken = default);
}
