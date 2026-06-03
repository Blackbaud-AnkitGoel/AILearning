using TextToSqlApi.Interfaces;
using TextToSqlApi.Models.Requests;

namespace TextToSqlApi.Validators;

/// <summary>
/// Validates an incoming <see cref="TextToSqlRequest"/> against business rules
/// before the request reaches the service layer.
/// </summary>
public sealed class TextToSqlRequestValidator : IRequestValidator
{
    private static readonly IReadOnlySet<string> SupportedDialects =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "T-SQL", "PostgreSQL", "MySQL", "SQLite", "Oracle"
        };

    private const int MaxQueryLength      = 2000;
    private const int MaxSchemaLength     = 10_000;
    private const int MaxRowsUpperLimit   = 10_000;

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(TextToSqlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();

        // --- NaturalLanguageQuery ---
        if (string.IsNullOrWhiteSpace(request.NaturalLanguageQuery))
        {
            errors.Add($"{nameof(request.NaturalLanguageQuery)} must not be empty.");
        }
        else if (request.NaturalLanguageQuery.Length > MaxQueryLength)
        {
            errors.Add($"{nameof(request.NaturalLanguageQuery)} must not exceed {MaxQueryLength} characters " +
                       $"(received {request.NaturalLanguageQuery.Length}).");
        }

        // --- SqlDialect ---
        if (string.IsNullOrWhiteSpace(request.SqlDialect))
        {
            errors.Add($"{nameof(request.SqlDialect)} must not be empty.");
        }
        else if (!SupportedDialects.Contains(request.SqlDialect))
        {
            errors.Add($"'{request.SqlDialect}' is not a supported SQL dialect. " +
                       $"Supported values: {string.Join(", ", SupportedDialects)}.");
        }

        // --- MaxRows ---
        if (request.MaxRows <= 0)
        {
            errors.Add($"{nameof(request.MaxRows)} must be greater than 0.");
        }
        else if (request.MaxRows > MaxRowsUpperLimit)
        {
            errors.Add($"{nameof(request.MaxRows)} must not exceed {MaxRowsUpperLimit}.");
        }

        // --- SchemaContext (optional, length-capped) ---
        if (request.SchemaContext is not null && request.SchemaContext.Length > MaxSchemaLength)
        {
            errors.Add($"{nameof(request.SchemaContext)} must not exceed {MaxSchemaLength} characters " +
                       $"(received {request.SchemaContext.Length}).");
        }

        return errors.AsReadOnly();
    }
}
