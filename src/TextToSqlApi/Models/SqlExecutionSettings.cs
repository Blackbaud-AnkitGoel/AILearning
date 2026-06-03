namespace TextToSqlApi.Models;

/// <summary>
/// Strongly-typed configuration for the SQL execution pipeline.
/// Bind this section from <c>appsettings.json</c> under the key
/// <see cref="SectionName"/>.
/// </summary>
/// <example>
/// <code language="json">
/// "SqlExecution": {
///   "ConnectionStringName": "DefaultConnection",
///   "CommandTimeoutSeconds": 30,
///   "MaxRows": 1000
/// }
/// </code>
/// </example>
public sealed class SqlExecutionSettings
{
    /// <summary>The configuration section key used to bind this class.</summary>
    public const string SectionName = "SqlExecution";

    /// <summary>
    /// The name of the connection string entry in the <c>ConnectionStrings</c>
    /// configuration section that <see cref="SqlExecutionService"/> will resolve
    /// at runtime.
    /// </summary>
    /// <value>Defaults to <c>"DefaultConnection"</c>.</value>
    public string ConnectionStringName { get; init; } = "DefaultConnection";

    /// <summary>
    /// Maximum number of seconds to wait for a SQL command to complete before
    /// raising a timeout exception.
    /// </summary>
    /// <remarks>
    /// Set to <c>0</c> to disable the command timeout (not recommended for
    /// production environments).  Negative values are treated as <c>30</c>.
    /// </remarks>
    /// <value>Defaults to <c>30</c> seconds.</value>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Hard upper bound on the number of rows that the execution service will
    /// return, regardless of what the generated SQL requests.
    /// </summary>
    /// <remarks>
    /// When the result set exceeds this limit the service truncates silently and
    /// logs a warning.  This provides a defence-in-depth guard against runaway
    /// queries that inadvertently omit a TOP/LIMIT clause.
    /// </remarks>
    /// <value>Defaults to <c>1000</c> rows.</value>
    public int MaxRows { get; init; } = 1000;
}
