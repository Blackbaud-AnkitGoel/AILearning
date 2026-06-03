namespace TextToSqlApi.Models.Responses;

/// <summary>
/// Standard envelope used for all API error responses.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>Machine-readable error code (e.g., "VALIDATION_FAILED", "AI_UNAVAILABLE").</summary>
    public string ErrorCode { get; init; } = string.Empty;

    /// <summary>Human-readable description of the error.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Additional detail, populated only in non-production environments.</summary>
    public string? Detail { get; init; }

    /// <summary>Correlation ID echoed back from the request.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>UTC timestamp when the error occurred.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
