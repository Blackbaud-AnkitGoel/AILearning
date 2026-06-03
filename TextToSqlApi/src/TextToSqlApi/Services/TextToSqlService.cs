using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;
using TextToSqlApi.Models.Requests;
using TextToSqlApi.Models.Responses;

namespace TextToSqlApi.Services;

/// <summary>
/// Core service that orchestrates prompt construction and Semantic Kernel invocation
/// to translate natural-language queries into SQL statements.
/// </summary>
public sealed class TextToSqlService : ITextToSqlService
{
    private readonly IPromptBuilder _promptBuilder;
    private readonly IChatCompletionService _chatCompletion;
    private readonly GitHubModelsSettings _settings;
    private readonly ILogger<TextToSqlService> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="TextToSqlService"/>.
    /// </summary>
    /// <param name="promptBuilder">Prompt construction strategy.</param>
    /// <param name="chatCompletion">Semantic Kernel chat completion service.</param>
    /// <param name="settings">Azure OpenAI configuration options.</param>
    /// <param name="logger">Structured logger.</param>
    public TextToSqlService(
        IPromptBuilder promptBuilder,
        IChatCompletionService chatCompletion,
        IOptions<GitHubModelsSettings> settings,
        ILogger<TextToSqlService> logger)
    {
        _promptBuilder    = promptBuilder    ?? throw new ArgumentNullException(nameof(promptBuilder));
        _chatCompletion   = chatCompletion   ?? throw new ArgumentNullException(nameof(chatCompletion));
        _settings         = settings?.Value  ?? throw new ArgumentNullException(nameof(settings));
        _logger           = logger           ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TextToSqlResponse> TranslateAsync(
        TextToSqlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var _ = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = request.CorrelationId,
            ["SqlDialect"]    = request.SqlDialect
        });

        _logger.LogInformation("Starting Text-to-SQL translation for query of length {QueryLength}",
            request.NaturalLanguageQuery.Length);

        var chatHistory = new ChatHistory();

        chatHistory.AddSystemMessage(
            _promptBuilder.BuildSystemPrompt(request.SqlDialect));

        chatHistory.AddUserMessage(
            _promptBuilder.BuildUserPrompt(
                request.NaturalLanguageQuery,
                request.SchemaContext,
                request.MaxRows));

        var executionSettings = new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"]  = _settings.Temperature,
                ["max_tokens"]   = _settings.MaxTokens
            }
        };

        _logger.LogDebug("Invoking AI model deployment '{Deployment}'", _settings.DeploymentName);

        var result = await _chatCompletion
            .GetChatMessageContentAsync(chatHistory, executionSettings, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var generatedSql = result.Content?.Trim() ?? string.Empty;
        var tokenUsage   = result.Metadata?.TryGetValue("Usage", out var usage) == true
            ? usage?.ToString()
            : null;

        _logger.LogInformation("Text-to-SQL translation completed. TokenUsage={TokenUsage}", tokenUsage);

        return new TextToSqlResponse
        {
            GeneratedSql    = generatedSql,
            OriginalQuery   = request.NaturalLanguageQuery,
            SqlDialect      = request.SqlDialect,
            CorrelationId   = request.CorrelationId,
            ConfidenceScore = EstimateConfidence(generatedSql),
            TotalTokensUsed = ParseTokenCount(tokenUsage)
        };
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Heuristic confidence estimate based on whether the result looks like valid SQL.
    /// Replace with a model-based approach when token-level logprobs are available.
    /// </summary>
    private static double EstimateConfidence(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return 0.0;

        var upper = sql.ToUpperInvariant();
        var hasDml = upper.Contains("SELECT") || upper.Contains("INSERT") ||
                     upper.Contains("UPDATE") || upper.Contains("DELETE");

        return hasDml ? 0.9 : 0.5;
    }

    private static int ParseTokenCount(string? raw)
    {
        if (raw is null) return 0;
        // Semantic Kernel surfaces usage as a typed object; adjust parsing per version.
        return 0;
    }
}
