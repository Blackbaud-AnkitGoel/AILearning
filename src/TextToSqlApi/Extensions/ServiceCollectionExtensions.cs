using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Polly;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;
using TextToSqlApi.Prompts;
using TextToSqlApi.Services;
using TextToSqlApi.Validators;

namespace TextToSqlApi.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> that register all
/// application-level services, options, Semantic Kernel, Polly, and Swagger.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all application services following the clean-architecture
    /// dependency graph.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Configuration binding ─────────────────────────────────────────
        services.Configure<AzureOpenAiSettings>(
            configuration.GetSection(AzureOpenAiSettings.SectionName));

        services.Configure<SqlExecutionSettings>(
            configuration.GetSection(SqlExecutionSettings.SectionName));

        services.Configure<ResilienceSettings>(
            configuration.GetSection(ResilienceSettings.SectionName));

        // ── Infrastructure ────────────────────────────────────────────────
        services.AddMemoryCache(opts => opts.SizeLimit = 50);

        // ── Semantic Kernel ───────────────────────────────────────────────
        services.AddSemanticKernel(configuration);

        // ── Domain services ───────────────────────────────────────────────
        services.AddScoped<IPromptBuilder, TextToSqlPromptBuilder>();
        services.AddScoped<ISqlValidator, SqlValidator>();
        services.AddScoped<ISqlExecutionService, SqlExecutionService>();
        services.AddScoped<IDatabaseSchemaService, DatabaseSchemaService>();
        services.AddScoped<IResultSummaryService, ResultSummaryService>();

        // TextToSqlService wrapped with the Polly resilience decorator.
        services.AddScoped<TextToSqlService>();
        services.AddScoped<ITextToSqlService>(sp =>
            new ResilientTextToSqlService(
                sp.GetRequiredService<TextToSqlService>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ResilienceSettings>>(),
                sp.GetRequiredService<ILogger<ResilientTextToSqlService>>()));

        // Request validator (TextToSqlRequest)
        services.AddScoped<IRequestValidator, TextToSqlRequestValidator>();

        // ── HTTP / Swagger ────────────────────────────────────────────────
        services.AddSwaggerDocumentation();

        return services;
    }

    /// <summary>
    /// Configures Semantic Kernel with an OpenAI-compatible backend (GitHub Models).
    /// </summary>
    internal static IServiceCollection AddSemanticKernel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration
            .GetSection(AzureOpenAiSettings.SectionName)
            .Get<AzureOpenAiSettings>()
            ?? throw new InvalidOperationException(
                $"Missing configuration section '{AzureOpenAiSettings.SectionName}'.");

        ValidateSemanticKernelSettings(settings);

        var kernelBuilder = Kernel.CreateBuilder();

        kernelBuilder.AddOpenAIChatCompletion(
            modelId: settings.DeploymentName,
            apiKey: settings.ApiKey,
            endpoint: new Uri(settings.Endpoint));

        Kernel kernel = kernelBuilder.Build();

        services.AddSingleton(kernel);
        services.AddSingleton(kernel.GetRequiredService<IChatCompletionService>());

        return services;
    }

    /// <summary>
    /// Adds Swagger/OpenAPI with full XML documentation, response examples, and metadata.
    /// </summary>
    internal static IServiceCollection AddSwaggerDocumentation(
        this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Text-to-SQL AI API",
                Version = "v1",
                Description =
                    "Enterprise API that translates natural-language questions into safe SQL queries " +
                    "using GitHub Models (GPT-4o via Semantic Kernel), executes them against SQL Server, " +
                    "and returns both the raw data and a business-friendly AI-generated summary.",
                Contact = new OpenApiContact
                {
                    Name = "Platform Engineering",
                    Email = "platform@example.com"
                }
            });

            // Include XML comments from the assembly.
            string xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);

            options.EnableAnnotations();

            // Standard API-key security scheme (used when auth is added later).
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Name = "X-Api-Key",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "API key authentication header."
            });
        });

        return services;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void ValidateSemanticKernelSettings(AzureOpenAiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
            throw new InvalidOperationException("OpenAI:Endpoint is required.");
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("OpenAI:ApiKey is required.");
        if (string.IsNullOrWhiteSpace(settings.DeploymentName))
            throw new InvalidOperationException("OpenAI:DeploymentName is required.");
    }
}
