using System.ComponentModel.DataAnnotations;

namespace TextToSqlApi.Models;

/// <summary>
/// Strongly-typed, validated configuration for the GitHub Models OpenAI-compatible endpoint.
/// Bound from the <c>GitHubModels</c> section of appsettings.json via
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
/// </summary>
public sealed class GitHubModelsSettings
{
    /// <summary>Configuration section key.</summary>
    public const string SectionName = "GitHubModels";

    /// <summary>
    /// OpenAI-compatible inference endpoint.
    /// GitHub Models: <c>https://models.inference.ai.azure.com</c>
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "GitHubModels:Endpoint is required.")]
    [Url(ErrorMessage = "GitHubModels:Endpoint must be a valid absolute URL.")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// GitHub Personal Access Token (classic or fine-grained) used to authenticate requests.
    /// Store this value in a secret store — never commit it to source control.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "GitHubModels:ApiKey is required.")]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Chat-completion model identifier (e.g., <c>gpt-4o</c>, <c>gpt-4o-mini</c>).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "GitHubModels:DeploymentName is required.")]
    public string DeploymentName { get; init; } = string.Empty;

    /// <summary>API version string (used by Azure-hosted deployments).</summary>
    public string ApiVersion { get; init; } = "2024-02-15-preview";

    /// <summary>Maximum tokens per completion response. Must be between 1 and 32 000.</summary>
    [Range(1, 32_000, ErrorMessage = "GitHubModels:MaxTokens must be between 1 and 32000.")]
    public int MaxTokens { get; init; } = 2000;

    /// <summary>Sampling temperature. 0 = deterministic, 2 = most creative.</summary>
    [Range(0.0, 2.0, ErrorMessage = "GitHubModels:Temperature must be between 0.0 and 2.0.")]
    public double Temperature { get; init; } = 0.0;

    /// <summary>
    /// Text-embedding model identifier (e.g., <c>text-embedding-3-small</c>).
    /// Leave empty to skip embedding-service registration.
    /// </summary>
    public string EmbeddingModelId { get; init; } = string.Empty;
}
