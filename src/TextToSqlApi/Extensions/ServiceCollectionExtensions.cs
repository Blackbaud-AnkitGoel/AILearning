using Microsoft.OpenApi.Models;
using System.Reflection;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;
using TextToSqlApi.Prompts;
using TextToSqlApi.Services;
using TextToSqlApi.Validators;
using Microsoft.SemanticKernel;

namespace TextToSqlApi.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> that register all
/// application-level services, options, Semantic Kernel, and Swagger/OpenAPI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers application services following the clean-architecture dependency graph.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Options ---
        services.Configure<AzureOpenAiSettings>(
            configuration.GetSection(AzureOpenAiSettings.SectionName));

        // --- Core services ---
        services.AddScoped<ITextToSqlService, TextToSqlService>();
        services.AddScoped<IRequestValidator, TextToSqlRequestValidator>();
        services.AddSingleton<IPromptBuilder,  TextToSqlPromptBuilder>();

        return services;
    }

    /// <summary>
    /// Configures Semantic Kernel with an OpenAI-compatible backend (e.g. GitHub Models).
    /// Both the <see cref="Kernel"/> and its chat-completion service are registered as
    /// <b>singletons</b>: the kernel is thread-safe, its HTTP client is reused across
    /// requests, and configuration is read once at startup.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when required configuration values are missing or invalid.
    /// </exception>
    public static IServiceCollection AddSemanticKernel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration
            .GetSection(AzureOpenAiSettings.SectionName)
            .Get<AzureOpenAiSettings>()
            ?? throw new InvalidOperationException(
                $"Missing required configuration section '{AzureOpenAiSettings.SectionName}'. " +
                $"Ensure appsettings.json contains an '{AzureOpenAiSettings.SectionName}' block.");

        ValidateSemanticKernelSettings(settings);

        var endpoint = new Uri(settings.Endpoint, UriKind.Absolute);

        var kernelBuilder = Kernel.CreateBuilder();

        // ── Chat completion ──────────────────────────────────────────────────
        // Uses the OpenAI-compatible REST surface exposed by GitHub Models.
        // Swap 'endpoint' for null (or remove the argument) to target openai.com directly.
        // SKEXP0010: custom-endpoint overload is experimental in SK 1.x but stable in practice.
#pragma warning disable SKEXP0010
        kernelBuilder.AddOpenAIChatCompletion(
            modelId:  settings.DeploymentName,
            apiKey:   settings.ApiKey,
            endpoint: endpoint);
#pragma warning restore SKEXP0010

        // ── Future: text-embedding generation ───────────────────────────────
        // 1. Set 'EmbeddingModelId' in appsettings.json (e.g. "text-embedding-3-small").
        // 2. Add the NuGet package Microsoft.SemanticKernel.Connectors.OpenAI if not present.
        // 3. Uncomment the block below.
        //
        // if (!string.IsNullOrWhiteSpace(settings.EmbeddingModelId))
        // {
        //     kernelBuilder.AddOpenAITextEmbeddingGeneration(
        //         modelId:  settings.EmbeddingModelId,
        //         apiKey:   settings.ApiKey,
        //         endpoint: endpoint);
        //
        //     services.AddSingleton(sp =>
        //         sp.GetRequiredService<Kernel>()
        //           .GetRequiredService<Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService>());
        // }

        var kernel = kernelBuilder.Build();

        // Singleton registrations — Kernel is immutable after Build() and safe for concurrent use.
        services.AddSingleton(kernel);

        services.AddSingleton(
            kernel.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>());

        return services;
    }

    /// <summary>
    /// Validates that all required Semantic Kernel settings are present and well-formed.
    /// Called once at startup to surface misconfigurations early.
    /// </summary>
    private static void ValidateSemanticKernelSettings(AzureOpenAiSettings settings)
    {
        var section = AzureOpenAiSettings.SectionName;

        if (string.IsNullOrWhiteSpace(settings.Endpoint))
            throw new InvalidOperationException(
                $"'{section}:{nameof(settings.Endpoint)}' is required and must not be empty.");

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out _))
            throw new InvalidOperationException(
                $"'{section}:{nameof(settings.Endpoint)}' must be a valid absolute URI. " +
                $"Received: '{settings.Endpoint}'.");

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException(
                $"'{section}:{nameof(settings.ApiKey)}' is required and must not be empty.");

        if (string.IsNullOrWhiteSpace(settings.DeploymentName))
            throw new InvalidOperationException(
                $"'{section}:{nameof(settings.DeploymentName)}' is required and must not be empty.");
    }

    /// <summary>
    /// Adds Swagger/OpenAPI with full XML documentation, security scheme, and versioning headers.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title       = "Enterprise AI Text-to-SQL API",
                Version     = "v1",
                Description = "Converts natural-language questions into SQL statements " +
                              "using Azure OpenAI via Semantic Kernel.",
                Contact = new OpenApiContact
                {
                    Name  = "Platform Engineering",
                    Email = "platform@yourdomain.com"
                },
                License = new OpenApiLicense
                {
                    Name = "MIT",
                    Url  = new Uri("https://opensource.org/licenses/MIT")
                }
            });

            // Include XML comments from the API assembly.
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            // API-Key security definition (optional; swap for OAuth2/OIDC in production).
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Description = "API key passed in the X-Api-Key header.",
                Name        = "X-Api-Key",
                In          = ParameterLocation.Header,
                Type        = SecuritySchemeType.ApiKey,
                Scheme      = "ApiKeyScheme"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id   = "ApiKey"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}
