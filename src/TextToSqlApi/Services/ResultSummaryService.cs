using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TextToSqlApi.Interfaces;

namespace TextToSqlApi.Services;

/// <summary>
/// Uses Semantic Kernel and a GitHub Models–backed chat completion service to
/// produce a business-friendly English summary of SQL query results.
/// </summary>
/// <remarks>
/// The prompt template is loaded once at construction time from
/// <c>Prompts/result-summary.txt</c>, relative to the application's content root.
/// The placeholders <c>{{$question}}</c> and <c>{{$results}}</c> are substituted
/// per call before the request is sent to the AI model.
/// </remarks>
public sealed class ResultSummaryService : IResultSummaryService
{
    private const string PromptFileName = "Prompts/result-summary.txt";
    private const int MaxSummaryTokens = 500;

    private readonly IChatCompletionService _chatCompletion;
    private readonly ILogger<ResultSummaryService> _logger;
    private readonly string _promptTemplate;

    /// <summary>
    /// Initialises a new instance of <see cref="ResultSummaryService"/>.
    /// </summary>
    /// <param name="chatCompletion">Semantic Kernel chat completion service (GitHub Models).</param>
    /// <param name="hostEnvironment">Provides the content-root path for locating prompt files.</param>
    /// <param name="logger">Structured logger.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the prompt file cannot be found at the expected path.
    /// </exception>
    public ResultSummaryService(
        IChatCompletionService chatCompletion,
        IWebHostEnvironment hostEnvironment,
        ILogger<ResultSummaryService> logger)
    {
        ArgumentNullException.ThrowIfNull(chatCompletion);
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        ArgumentNullException.ThrowIfNull(logger);

        _chatCompletion = chatCompletion;
        _logger = logger;

        string promptPath = Path.Combine(hostEnvironment.ContentRootPath, PromptFileName);

        if (!File.Exists(promptPath))
        {
            throw new InvalidOperationException(
                $"Result-summary prompt file not found at '{promptPath}'. " +
                "Ensure the file is present and marked as 'Copy to Output Directory'.");
        }

        _promptTemplate = File.ReadAllText(promptPath);
        _logger.LogInformation("ResultSummaryService initialised. PromptPath={PromptPath}", promptPath);
    }

    /// <inheritdoc />
    public async Task<string> SummarizeAsync(
        string question,
        string resultsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsJson);

        cancellationToken.ThrowIfCancellationRequested();

        // Substitute the prompt placeholders.
        string populatedPrompt = _promptTemplate
            .Replace("{{$question}}", question, StringComparison.Ordinal)
            .Replace("{{$results}}", resultsJson, StringComparison.Ordinal);

        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(populatedPrompt);

        var executionSettings = new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                ["max_tokens"] = MaxSummaryTokens,
                ["temperature"] = 0.3   // Slightly creative for natural language, but grounded
            }
        };

        _logger.LogInformation(
            "Requesting result summary from AI model. QuestionLength={QuestionLength}, ResultsLength={ResultsLength}",
            question.Length,
            resultsJson.Length);

        try
        {
            IReadOnlyList<ChatMessageContent> responses = await _chatCompletion
                .GetChatMessageContentsAsync(chatHistory, executionSettings, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            string summary = responses.FirstOrDefault()?.Content?.Trim()
                ?? "No summary could be generated for the provided results.";

            _logger.LogInformation(
                "Result summary generated successfully. SummaryLength={SummaryLength}",
                summary.Length);

            return summary;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Result summary request was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate result summary from AI model.");
            throw;
        }
    }
}
