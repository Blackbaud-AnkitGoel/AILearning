namespace TextToSqlApi.Extensions;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> that configure
/// the HTTP request pipeline for the Text-to-SQL API.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Registers Swagger UI and the OpenAPI JSON endpoint.
    /// Only mounted in non-production environments by default; set
    /// <c>Swagger:AlwaysEnable</c> to <c>true</c> in configuration to override.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">The host configuration (used to check the override flag).</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
    public static IApplicationBuilder UseSwaggerDocumentation(
        this IApplicationBuilder app,
        IConfiguration configuration)
    {
        var alwaysEnable = configuration.GetValue<bool>("Swagger:AlwaysEnable");

        var env = app.ApplicationServices
            .GetRequiredService<IWebHostEnvironment>();

        if (env.IsDevelopment() || alwaysEnable)
        {
            app.UseSwagger();
            app.UseSwaggerUI(ui =>
            {
                ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Text-to-SQL API v1");
                ui.RoutePrefix      = "swagger";
                ui.DocumentTitle    = "Enterprise AI Text-to-SQL API";
                ui.DisplayRequestDuration();
            });
        }

        return app;
    }

    /// <summary>
    /// Registers a global exception-handling middleware that converts unhandled exceptions
    /// into RFC 7807–style Problem Details responses.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILogger<IApplicationBuilder>>();

                var feature = context.Features
                    .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

                if (feature?.Error is not null)
                {
                    logger.LogError(feature.Error,
                        "Unhandled exception on {Method} {Path}",
                        context.Request.Method,
                        context.Request.Path);
                }

                context.Response.StatusCode  = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";

                var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
                var body = System.Text.Json.JsonSerializer.Serialize(new
                {
                    ErrorCode     = "INTERNAL_SERVER_ERROR",
                    Message       = "An unexpected error occurred. Please try again later.",
                    Detail        = env.IsDevelopment() ? feature?.Error?.Message : null,
                    OccurredAt    = DateTimeOffset.UtcNow
                });

                await context.Response.WriteAsync(body).ConfigureAwait(false);
            });
        });

        return app;
    }
}
