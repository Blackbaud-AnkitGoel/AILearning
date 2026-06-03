using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using System.Reflection;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;
using TextToSqlApi.Prompts;
using TextToSqlApi.Services;
using TextToSqlApi.Validators;

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
        // ── Strongly-typed options with eager DataAnnotations validation ────────
        // ValidateOnStart() causes the host to fail immediately at startup if any
        // required value is missing or out of range — no silent misconfiguration.
        services.AddOptions<GitHubModelsSettings>()
            .BindConfiguration(GitHubModelsSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ConnectionStringsSettings>()
            .BindConfiguration(ConnectionStringsSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Core services ────────────────────────────────────────────────────────
        services.AddScoped<ITextToSqlService, TextToSqlService>();
        services.AddScoped<IRequestValidator, TextToSqlRequestValidator>();
        services.AddSingleton<IPromptBuilder, TextToSqlPromptBuilder>();

        // Singleton: prompt template loaded once from Prompts/sql-generation.txt;
        // Kernel and IHostEnvironment are both singletons, so this is safe.
        services.AddSingleton<ISqlGenerationService, SqlGenerationService>();

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
        // Kernel is registered as a singleton — it is immutable after Build() and thread-safe.
        // Settings are resolved from IOptions<GitHubModelsSettings> which has already been
        // validated (DataAnnotations + ValidateOnStart) before this factory runs.
        services.AddSingleton<Kernel>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<GitHubModelsSettings>>().Value;
            var endpoint = new Uri(settings.Endpoint, UriKind.Absolute);

            var kernelBuilder = Kernel.CreateBuilder();

            // ── Chat completion ──────────────────────────────────────────────
            // OpenAI-compatible surface — works with GitHub Models, openai.com, and Azure OpenAI.
            // SKEXP0010: custom-endpoint overload is experimental in SK 1.x but stable in practice.
#pragma warning disable SKEXP0010
            kernelBuilder.AddOpenAIChatCompletion(
                modelId:  settings.DeploymentName,
                apiKey:   settings.ApiKey,
                endpoint: endpoint);
#pragma warning restore SKEXP0010

            // ── Future: text-embedding generation ───────────────────────────
            // 1. Set 'EmbeddingModelId' in appsettings.json (e.g. "text-embedding-3-small").
            // 2. Uncomment the block below.
            //
            // if (!string.IsNullOrWhiteSpace(settings.EmbeddingModelId))
            // {
            //     kernelBuilder.AddOpenAITextEmbeddingGeneration(
            //         modelId:  settings.EmbeddingModelId,
            //         apiKey:   settings.ApiKey,
            //         endpoint: endpoint);
            // }

            return kernelBuilder.Build();
        });

        // Expose IChatCompletionService directly so consumers receive it via constructor injection.
        services.AddSingleton(sp =>
            sp.GetRequiredService<Kernel>()
              .GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>());

        // ── Future: expose ITextEmbeddingGenerationService ───────────────────
        // Uncomment when EmbeddingModelId is configured.
        //
        // services.AddSingleton(sp =>
        //     sp.GetRequiredService<Kernel>()
        //       .GetRequiredService<Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService>());

        return services;
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
