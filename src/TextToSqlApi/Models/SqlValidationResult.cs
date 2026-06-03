namespace TextToSqlApi.Models;

/// <summary>
/// Represents the outcome of an SQL safety-validation check performed by
/// <see cref="TextToSqlApi.Validators.SqlValidator"/>.
/// </summary>
/// <remarks>
/// Use the static factory methods <see cref="Success"/> and <see cref="Failure"/> to
/// construct instances; the constructor is intentionally private to enforce invariants.
/// </remarks>
public sealed class SqlValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the validated SQL passed all safety checks.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets a human-readable description of the first validation failure, or
    /// <see langword="null"/> when <see cref="IsValid"/> is <see langword="true"/>.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the (potentially modified) SQL string returned by the validator.
    /// May differ from the original input if the validator injected a TOP clause.
    /// <see langword="null"/> when <see cref="IsValid"/> is <see langword="false"/>.
    /// </summary>
    public string? NormalizedSql { get; }

    private SqlValidationResult(bool isValid, string? errorMessage, string? normalizedSql = null)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        NormalizedSql = normalizedSql;
    }

    /// <summary>
    /// Creates a successful validation result, optionally carrying the normalised SQL.
    /// </summary>
    /// <param name="normalizedSql">
    /// The SQL string after any normalisation applied by the validator
    /// (e.g., TOP injection).  When <see langword="null"/> the original input is unchanged.
    /// </param>
    /// <returns>A <see cref="SqlValidationResult"/> with <see cref="IsValid"/> set to
    /// <see langword="true"/>.</returns>
    public static SqlValidationResult Success(string? normalizedSql = null) => new(true, null, normalizedSql);

    /// <summary>
    /// Creates a failed validation result with a descriptive error message.
    /// </summary>
    /// <param name="errorMessage">A non-null human-readable reason for the failure.</param>
    /// <returns>A <see cref="SqlValidationResult"/> with <see cref="IsValid"/> set to
    /// <see langword="false"/> and <see cref="ErrorMessage"/> set to
    /// <paramref name="errorMessage"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="errorMessage"/> is <see langword="null"/>.
    /// </exception>
    public static SqlValidationResult Failure(string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(errorMessage);
        return new(false, errorMessage);
    }
}
