using Microsoft.AspNetCore.HttpLogging;
using Serilog;
using Serilog.Events;
using TextToSqlApi.Extensions;

// ============================================================================
// Bootstrap Serilog early so that startup errors are captured.
// ============================================================================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting TextToSqlApi host");

    var builder = WebApplication.CreateBuilder(args);

    // ========================================================================
    // Logging — replace built-in logging with Serilog
    // ========================================================================
    builder.Host.UseSerilog((context, services, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "TextToSqlApi")
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);
    });

    // HTTP request logging (duration, status code, path).
    builder.Services.AddHttpLogging(logging =>
    {
        logging.LoggingFields =
            HttpLoggingFields.RequestMethod |
            HttpLoggingFields.RequestPath   |
            HttpLoggingFields.ResponseStatusCode |
            HttpLoggingFields.Duration;
    });

    // ========================================================================
    // Controllers + JSON
    // ========================================================================
    builder.Services
        .AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;
            opts.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

    // ========================================================================
    // Application services (DI registrations)
    // ========================================================================
    builder.Services
        .AddApplicationServices(builder.Configuration)
        .AddSemanticKernel(builder.Configuration)
        .AddSwaggerDocumentation();

    // ========================================================================
    // Health checks
    // ========================================================================
    builder.Services.AddHealthChecks();

    // ========================================================================
    // CORS — tighten origins in production via configuration
    // ========================================================================
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowConfigured", policy =>
        {
            var origins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? [];

            if (origins.Length == 0)
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            }
            else
            {
                policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
            }
        });
    });

    // ========================================================================
    // Build the pipeline
    // ========================================================================
    var app = builder.Build();

    app.UseGlobalExceptionHandler();

    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    });

    app.UseSwaggerDocumentation(builder.Configuration);

    app.UseHttpsRedirection();
    app.UseCors("AllowConfigured");
    app.UseHttpLogging();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "TextToSqlApi terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
