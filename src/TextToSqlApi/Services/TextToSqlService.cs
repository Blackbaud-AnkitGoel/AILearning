using Microsoft.Extensions.Options;
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
    private readonly AzureOpenAiSettings _settings;
    private readonly ILogger<TextToSqlService> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="TextToSqlService"/>.
    /// </summary>
    public TextToSqlService(
        IPromptBuilder promptBuilder,
        IChatCompletionService chatCompletion,
        IOptions<AzureOpenAiSettings> settings,
        ILogger<TextToSqlService> logger)
    {
        _promptBuilder = promptBuilder;
        _chatCompletion = chatCompletion;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TextToSqlResponse> TranslateAsync(
        TextToSqlRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Translating query. Dialect={Dialect} MaxRows={MaxRows} CorrelationId={CorrelationId}",
            request.SqlDialect, request.MaxRows, request.CorrelationId);

        string systemPrompt = _promptBuilder.BuildSystemPrompt(request.SqlDialect);
        string userPrompt = _promptBuilder.BuildUserPrompt(
            request.NaturalLanguageQuery,
            request.SchemaContext ?? string.Empty,
            request.MaxRows);

        var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        chatHistory.AddUserMessage(userPrompt);

        var executionSettings = new Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings
        {
            MaxTokens = _settings.MaxTokens > 0 ? _settings.MaxTokens : 1000,
            Temperature = _settings.Temperature,
        };

        var results = await _chatCompletion
            .GetChatMessageContentsAsync(chatHistory, executionSettings, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        string generatedSql = string.Concat(results.Select(r => r.Content)).Trim();

        // Strip markdown code fences if the model emits them despite instructions.
        generatedSql = StripMarkdownFences(generatedSql);

        // Attempt to extract total token usage from metadata if available.
        int totalTokens = 0;
        foreach (var result in results)
        {
            if (result.Metadata?.TryGetValue("Usage", out object? usage) == true && usage is not null)
            {
                // Try to read CompletionTokens + PromptTokens via reflection to stay version-agnostic.
                var usageType = usage.GetType();
                var totalProp = usageType.GetProperty("TotalTokenCount")
                             ?? usageType.GetProperty("TotalTokens");
                if (totalProp?.GetValue(usage) is int t)
                    totalTokens += t;
            }
        }

        _logger.LogInformation(
            "SQL translation complete. Tokens={Tokens} CorrelationId={CorrelationId}",
            totalTokens, request.CorrelationId);

        return new TextToSqlResponse
        {
            GeneratedSql = generatedSql,
            OriginalQuery = request.NaturalLanguageQuery,
            ConfidenceScore = EstimateConfidence(generatedSql),
            SqlDialect = request.SqlDialect,
            TotalTokensUsed = totalTokens,
            CorrelationId = request.CorrelationId
        };
    }

    /// <summary>
    /// Heuristic confidence estimate based on whether the result looks like valid SQL.
    /// </summary>
    private static double EstimateConfidence(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return 0.0;
        if (sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) return 0.9;
        return 0.5;
    }

    private static string StripMarkdownFences(string sql)
    {
        if (sql.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = sql.IndexOf('\n');
            if (firstNewline >= 0)
                sql = sql[(firstNewline + 1)..];
            if (sql.EndsWith("```", StringComparison.Ordinal))
                sql = sql[..^3];
        }
        return sql.Trim();
    }
}
