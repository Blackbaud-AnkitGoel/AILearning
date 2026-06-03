namespace TextToSqlApi.Models;

/// <summary>
/// Strongly-typed configuration for the Polly resilience policy applied to
/// outbound AI model (Semantic Kernel / GitHub Models) calls.
/// Bind from the <c>"Resilience"</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class ResilienceSettings
{
    /// <summary>The configuration section key used to bind this class.</summary>
    public const string SectionName = "Resilience";

    /// <summary>
    /// Maximum number of retry attempts after an initial failure.
    /// </summary>
    /// <value>Defaults to <c>3</c>.</value>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay in seconds for the first retry.
    /// Subsequent retries use exponential back-off: <c>BaseDelaySeconds * 2^attempt</c>.
    /// </summary>
    /// <value>Defaults to <c>2</c> seconds.</value>
    public int BaseDelaySeconds { get; init; } = 2;

    /// <summary>
    /// Maximum total timeout in seconds for a single AI call (all retry attempts
    /// included).  The call is cancelled if this limit is exceeded.
    /// </summary>
    /// <value>Defaults to <c>60</c> seconds.</value>
    public int TotalTimeoutSeconds { get; init; } = 60;
}
