using Microsoft.SemanticKernel;
using Polly;
using Polly.Retry;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;
using TextToSqlApi.Models.Requests;
using TextToSqlApi.Models.Responses;

namespace TextToSqlApi.Services;

/// <summary>
/// Decorator for <see cref="ITextToSqlService"/> that wraps every call in a
/// Polly resilience pipeline providing:
/// <list type="bullet">
///   <item><description>Exponential back-off retry for transient failures and rate limits.</description></item>
///   <item><description>Overall call timeout enforcement.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Transient conditions retried:
/// <list type="bullet">
///   <item><description><see cref="HttpRequestException"/> — network-level failures.</description></item>
///   <item><description><see cref="TimeoutException"/> — model endpoint unresponsive.</description></item>
///   <item><description>HTTP 429 / 503 responses surfaced as <see cref="KernelException"/>.</description></item>
/// </list>
/// </remarks>
public sealed class ResilientTextToSqlService : ITextToSqlService
{
    private readonly ITextToSqlService _inner;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly TimeSpan _totalTimeout;
    private readonly ILogger<ResilientTextToSqlService> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="ResilientTextToSqlService"/>.
    /// </summary>
    /// <param name="inner">The underlying <see cref="ITextToSqlService"/> to decorate.</param>
    /// <param name="settings">Resilience configuration (retry count, delays, timeout).</param>
    /// <param name="logger">Structured logger.</param>
    public ResilientTextToSqlService(
        ITextToSqlService inner,
        Microsoft.Extensions.Options.IOptions<ResilienceSettings> settings,
        ILogger<ResilientTextToSqlService> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _logger = logger;

        ResilienceSettings cfg = settings.Value;
        _totalTimeout = TimeSpan.FromSeconds(cfg.TotalTimeoutSeconds);

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .Or<KernelException>(ex => IsTransient(ex))
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .WaitAndRetryAsync(
                retryCount: cfg.MaxRetryAttempts,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(cfg.BaseDelaySeconds * Math.Pow(2, attempt - 1)),
                onRetry: (exception, delay, attempt, _) =>
                {
                    _logger.LogWarning(
                        exception,
                        "AI model call failed (attempt {Attempt}/{MaxAttempts}). " +
                        "Retrying in {DelaySeconds:F1}s. Error={Error}",
                        attempt,
                        cfg.MaxRetryAttempts,
                        delay.TotalSeconds,
                        exception.Message);
                });
    }

    /// <inheritdoc />
    public async Task<TextToSqlResponse> TranslateAsync(
        TextToSqlRequest request,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_totalTimeout);

        try
        {
            return await _retryPolicy.ExecuteAsync(
                ct => _inner.TranslateAsync(request, ct),
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "AI model call exceeded total timeout of {TimeoutSeconds}s.",
                _totalTimeout.TotalSeconds);

            throw new TimeoutException(
                $"The AI model did not respond within {_totalTimeout.TotalSeconds} second(s).");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> for <see cref="KernelException"/> instances
    /// that represent transient server-side conditions (rate limiting, service
    /// unavailable) rather than permanent errors (bad request, auth failure).
    /// </summary>
    private static bool IsTransient(KernelException ex)
    {
        string msg = ex.Message;
        return msg.Contains("429", StringComparison.Ordinal)   // Too Many Requests
            || msg.Contains("503", StringComparison.Ordinal)   // Service Unavailable
            || msg.Contains("502", StringComparison.Ordinal)   // Bad Gateway
            || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("throttl", StringComparison.OrdinalIgnoreCase);
    }
}
