using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using System.Text.RegularExpressions;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;

namespace TextToSqlApi.Services;

/// <summary>
/// AI-powered SQL generation service that uses Semantic Kernel and an external prompt template
/// to translate natural-language questions into SQL queries.
/// </summary>
/// <remarks>
/// The prompt template is loaded once from <c>Prompts/sql-generation.txt</c> at construction
/// time and compiled into a <see cref="KernelFunction"/>. The service is registered as a
/// singleton: the <see cref="Kernel"/> is thread-safe, and the compiled function is immutable.
/// </remarks>
public sealed class SqlGenerationService : ISqlGenerationService
{
    // Relative path from the content root to the SK prompt template.
    private const string PromptRelativePath = "Prompts/sql-generation.txt";

    // Matches ```sql ... ``` or ``` ... ``` blocks produced by over-eager models.
    // Uses Singleline so '.' spans newlines; compiled for repeated call performance.
    private static readonly Regex MarkdownFenceRegex = new(
        @"^```(?:sql)?\s*\r?\n?(.*?)\r?\n?```\s*$",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Kernel _kernel;
    private readonly GitHubModelsSettings _settings;
    private readonly ILogger<SqlGenerationService> _logger;
    private readonly KernelFunction _sqlFunction;

    /// <summary>
    /// Initialises a new instance of <see cref="SqlGenerationService"/>.
    /// Loads and compiles the prompt template from disk; throws immediately if the file
    /// is missing so the host fails at startup rather than on the first request.
    /// </summary>
    /// <param name="kernel">The Semantic Kernel instance with a registered chat-completion service.</param>
    /// <param name="settings">GitHub Models configuration options.</param>
    /// <param name="environment">Host environment used to resolve the content root path.</param>
    /// <param name="logger">Structured logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <c>Prompts/sql-generation.txt</c> does not exist under the content root.
    /// </exception>
    public SqlGenerationService(
        Kernel kernel,
        IOptions<GitHubModelsSettings> settings,
        IHostEnvironment environment,
        ILogger<SqlGenerationService> logger)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        _kernel   = kernel;
        _settings = settings.Value;
        _logger   = logger;

        // Load and compile once — subsequent calls reuse the cached KernelFunction.
        _sqlFunction = LoadPromptFunction(environment.ContentRootPath);
    }

    /// <inheritdoc />
    public async Task<string> GenerateSqlAsync(
        string question,
        string? schemaContext,
        string dialect,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentException.ThrowIfNullOrWhiteSpace(dialect);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["SqlDialect"]     = dialect,
            ["QuestionLength"] = question.Length
        });

        _logger.LogInformation(
            "Starting SQL generation. Dialect={Dialect}, HasSchema={HasSchema}",
            dialect, !string.IsNullOrWhiteSpace(schemaContext));

        var executionSettings = new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = _settings.Temperature,
                ["max_tokens"]  = _settings.MaxTokens
            }
        };

        var arguments = new KernelArguments(executionSettings)
        {
            ["question"] = question.Trim(),
            ["schema"]   = BuildSchemaSection(schemaContext),
            ["dialect"]  = dialect,
            ["maxRows"]  = maxRows.ToString()
        };

        try
        {
            _logger.LogDebug(
                "Invoking SQL generation function with model '{Model}'.",
                _settings.DeploymentName);

            var result = await _kernel
                .InvokeAsync(_sqlFunction, arguments, cancellationToken)
                .ConfigureAwait(false);

            var rawOutput = result.GetValue<string>() ?? string.Empty;

            _logger.LogDebug("Raw model response length: {Length} characters.", rawOutput.Length);

            var sql = StripMarkdownFences(rawOutput);

            _logger.LogInformation(
                "SQL generation completed. OutputLength={OutputLength}, IsCannot={IsCannot}",
                sql.Length,
                sql.Equals("CANNOT_GENERATE_SQL", StringComparison.OrdinalIgnoreCase));

            return sql;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("SQL generation was cancelled by the caller.");
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(
                ex,
                "SQL generation failed for dialect '{Dialect}'. Model='{Model}'.",
                dialect, _settings.DeploymentName);

            throw new InvalidOperationException(
                $"SQL generation failed while invoking the AI model: {ex.Message}", ex);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads the prompt template from disk and compiles it into a reusable
    /// <see cref="KernelFunction"/> using Semantic Kernel's templating engine.
    /// </summary>
    private KernelFunction LoadPromptFunction(string contentRootPath)
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(contentRootPath, PromptRelativePath));

        _logger.LogDebug("Loading SQL generation prompt from '{Path}'.", fullPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"SQL generation prompt template not found at '{fullPath}'. " +
                $"Ensure '{PromptRelativePath}' exists under the content root and is " +
                "configured with CopyToOutputDirectory=PreserveNewest in the project file.",
                fullPath);
        }

        var template = File.ReadAllText(fullPath);

        _logger.LogInformation(
            "SQL generation prompt loaded successfully from '{Path}'.", fullPath);

        return KernelFunctionFactory.CreateFromPrompt(
            template,
            functionName: "GenerateSql",
            description:  "Converts a natural-language question into a SQL query.");
    }

    /// <summary>
    /// Wraps non-empty schema context in clearly delimited markers so the model
    /// can parse the schema boundary reliably.
    /// Returns an empty string when no schema is provided, preserving the prompt
    /// template's whitespace without leaving stray section headers.
    /// </summary>
    private static string BuildSchemaSection(string? schemaContext)
    {
        if (string.IsNullOrWhiteSpace(schemaContext))
            return string.Empty;

        return $"""
            === DATABASE SCHEMA ===
            {schemaContext.Trim()}
            === END SCHEMA ===


            """;
    }

    /// <summary>
    /// Removes markdown code fences that some models insert around SQL output
    /// despite explicit instructions not to (e.g., <c>```sql\nSELECT ...\n```</c>).
    /// Returns the original trimmed string when no fence pattern is detected.
    /// </summary>
    private static string StripMarkdownFences(string raw)
    {
        var trimmed = raw.Trim();
        var match   = MarkdownFenceRegex.Match(trimmed);
        return match.Success ? match.Groups[1].Value.Trim() : trimmed;
    }
}
