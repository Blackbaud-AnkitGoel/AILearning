namespace TextToSqlApi.Models;

/// <summary>
/// Configuration settings for the Azure OpenAI / GitHub Models endpoint
/// used by Semantic Kernel.  Bound from the <c>"OpenAI"</c> configuration section.
/// </summary>
public sealed class AzureOpenAiSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "OpenAI";

    /// <summary>
    /// The OpenAI-compatible endpoint URL.
    /// For GitHub Models this is <c>https://models.inference.ai.azure.com</c>.
    /// </summary>
    public string Endpoint { get; init; } = "https://models.inference.ai.azure.com";

    /// <summary>The model / deployment name (e.g. <c>gpt-4o</c>).</summary>
    public string DeploymentName { get; init; } = "gpt-4o";

    /// <summary>Personal access token or Azure OpenAI API key.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>API version string (used by Azure-hosted deployments).</summary>
    public string? ApiVersion { get; init; }

    /// <summary>Maximum tokens allowed per completion request (0 = use model default).</summary>
    public int MaxTokens { get; init; } = 1000;

    /// <summary>Sampling temperature (0 = deterministic, 1 = creative).</summary>
    public double Temperature { get; init; } = 0.0;

    /// <summary>
    /// Text-embedding model identifier (e.g., "text-embedding-3-small").
    /// Leave empty to skip embedding-service registration.
    /// </summary>
    public string? EmbeddingModelId { get; init; }
}
