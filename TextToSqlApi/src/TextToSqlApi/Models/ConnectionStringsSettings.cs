using System.ComponentModel.DataAnnotations;

namespace TextToSqlApi.Models;

/// <summary>
/// Strongly-typed, validated configuration for database connection strings.
/// Bound from the standard <c>ConnectionStrings</c> section of appsettings.json via
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
/// </summary>
public sealed class ConnectionStringsSettings
{
    /// <summary>Configuration section key — maps to the built-in ASP.NET Core section.</summary>
    public const string SectionName = "ConnectionStrings";

    /// <summary>
    /// Primary read-write connection string.
    /// Required — the application cannot start without a valid database connection.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "ConnectionStrings:DefaultConnection is required.")]
    public string DefaultConnection { get; init; } = string.Empty;

    /// <summary>
    /// Optional read-only replica connection string.
    /// When set, read-heavy queries can be routed here to reduce primary load.
    /// </summary>
    public string? ReadOnlyConnection { get; init; }
}
