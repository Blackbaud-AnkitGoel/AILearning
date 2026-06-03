using TextToSqlApi.Models;

namespace TextToSqlApi.Interfaces;

/// <summary>
/// Defines the contract for validating an AI-generated SQL statement to ensure
/// it is safe for enterprise execution (SELECT-only, no destructive operations).
/// </summary>
/// <remarks>
/// Implementations must be stateless so that a single instance can be shared
/// across concurrent requests without synchronisation overhead.
/// </remarks>
public interface ISqlValidator
{
    /// <summary>
    /// Validates the supplied SQL statement against enterprise safety rules.
    /// </summary>
    /// <param name="sql">
    /// The SQL string to validate. May be <see langword="null"/> or whitespace,
    /// in which case validation fails with an appropriate message.
    /// </param>
    /// <returns>
    /// A <see cref="SqlValidationResult"/> whose <see cref="SqlValidationResult.IsValid"/>
    /// property is <see langword="true"/> when the statement passes all checks, or
    /// <see langword="false"/> with a descriptive <see cref="SqlValidationResult.ErrorMessage"/>
    /// on the first failure detected.
    /// When validation succeeds, <see cref="SqlValidationResult.NormalizedSql"/> contains
    /// the (possibly TOP-injected) SQL that should be executed in place of the original.
    /// </returns>
    SqlValidationResult Validate(string? sql);
}
