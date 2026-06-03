namespace TextToSqlApi.Models;

/// <summary>
/// Strongly-typed representation of the OpenAI-compatible configuration section in appsettings.
/// Supports GitHub Models and any other OpenAI-compatible inference endpoint.
/// </summary>
public sealed class AzureOpenAiSettings
{
    /// <summary>Configuration section key.</summary>
    public const string SectionName = "OpenAI";

    /// <summary>
    /// OpenAI-compatible endpoint URL.
    /// For GitHub Models use: https://models.inference.ai.azure.com
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// API key used to authenticate requests.
    /// For GitHub Models, supply a GitHub Personal Access Token (classic or fine-grained).
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Chat-completion model identifier (e.g., "gpt-4o", "gpt-4o-mini").</summary>
    public string DeploymentName { get; init; } = string.Empty;

    /// <summary>API version string (used by Azure-hosted deployments).</summary>
    public string ApiVersion { get; init; } = "2024-02-15-preview";

    /// <summary>Maximum tokens allowed per completion request.</summary>
    public int MaxTokens { get; init; } = 2000;

    /// <summary>Sampling temperature (0 = deterministic, 1 = creative).</summary>
    public double Temperature { get; init; } = 0.0;

    /// <summary>
    /// Text-embedding model identifier (e.g., "text-embedding-3-small").
    /// Leave empty to skip embedding-service registration.
    /// </summary>
    public string EmbeddingModelId { get; init; } = string.Empty;
}
