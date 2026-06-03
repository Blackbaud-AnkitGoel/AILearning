using TextToSqlApi.Interfaces;
using TextToSqlApi.Models.Requests;

namespace TextToSqlApi.Validators;

/// <summary>
/// Validates an incoming <see cref="TextToSqlRequest"/> against business rules
/// before the request reaches the service layer.
/// </summary>
public sealed class TextToSqlRequestValidator : IRequestValidator
{
    private const int MaxQueryLength = 2000;
    private const int MaxSchemaContextLength = 50_000;

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(TextToSqlRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.NaturalLanguageQuery))
            errors.Add("NaturalLanguageQuery is required.");
        else if (request.NaturalLanguageQuery.Length < 3)
            errors.Add("NaturalLanguageQuery must be at least 3 characters.");
        else if (request.NaturalLanguageQuery.Length > MaxQueryLength)
            errors.Add($"NaturalLanguageQuery must not exceed {MaxQueryLength} characters.");

        if (request.SchemaContext is not null &&
            request.SchemaContext.Length > MaxSchemaContextLength)
            errors.Add($"SchemaContext must not exceed {MaxSchemaContextLength} characters.");

        if (request.MaxRows is < 1 or > 5000)
            errors.Add("MaxRows must be between 1 and 5000.");

        return errors.AsReadOnly();
    }
}
