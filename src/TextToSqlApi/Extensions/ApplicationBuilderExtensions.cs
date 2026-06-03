namespace TextToSqlApi.Extensions;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> that configure
/// the HTTP request pipeline for the Text-to-SQL API.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Registers Swagger UI and the OpenAPI JSON endpoint.
    /// Enabled in all environments by default; set <c>Swagger:AlwaysEnable</c>
    /// to <c>false</c> to disable it in production.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The same <see cref="IApplicationBuilder"/> for chaining.</returns>
    public static IApplicationBuilder UseSwaggerDocumentation(
        this IApplicationBuilder app,
        IConfiguration configuration)
    {
        bool alwaysEnable = configuration.GetValue("Swagger:AlwaysEnable", defaultValue: true);
        var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();

        if (alwaysEnable || !env.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Text-to-SQL AI API v1");
                c.RoutePrefix = "swagger";
                c.DocumentTitle = "Text-to-SQL AI API";
                c.DisplayRequestDuration();
                c.EnableDeepLinking();
            });
        }

        return app;
    }
}
