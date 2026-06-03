using TextToSqlApi.Models.Requests;

namespace TextToSqlApi.Interfaces;

/// <summary>
/// Validates an incoming <see cref="TextToSqlRequest"/> before it is processed by the service layer.
/// </summary>
public interface IRequestValidator
{
    /// <summary>
    /// Validates the provided request and returns a collection of validation errors.
    /// An empty collection indicates the request is valid.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A read-only list of human-readable validation error messages.</returns>
    IReadOnlyList<string> Validate(TextToSqlRequest request);
}
