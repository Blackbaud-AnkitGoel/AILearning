using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;

namespace TextToSqlApi.Services;

/// <summary>
/// Executes pre-validated SQL <c>SELECT</c> queries against a SQL Server database
/// using Dapper and returns the results as a dynamic collection.
/// </summary>
/// <remarks>
/// <para>
/// <strong>SQL-injection prevention</strong>: This service accepts only a plain SQL
/// string; it never interpolates user-supplied values into the command text.  The
/// AI-generated query must have been validated by <see cref="ISqlValidator"/> before
/// reaching this service.  Because the query itself is the AI output (not a raw user
/// string concatenation), parameterisation is not applicable here; the defence-in-
/// depth layers are (a) the SELECT-only validator and (b) database-level least-
/// privilege permissions on the configured login.
/// </para>
/// <para>
/// <strong>Connection management</strong>: A new <see cref="SqlConnection"/> is
/// opened per call and disposed on completion via a <c>using</c> block, ensuring
/// connections are returned to the ADO.NET pool promptly.
/// </para>
/// <para>
/// <strong>Cancellation</strong>: The <see cref="CancellationToken"/> is forwarded
/// to Dapper's <see cref="CommandDefinition"/>, which sets it on the underlying
/// <see cref="System.Data.IDbCommand"/> so that ADO.NET can abort the in-flight
/// command on the server side.
/// </para>
/// </remarks>
public sealed class SqlExecutionService : ISqlExecutionService
{
    // -------------------------------------------------------------------------
    // Dependencies
    // -------------------------------------------------------------------------

    private readonly IConfiguration _configuration;
    private readonly SqlExecutionSettings _settings;
    private readonly ILogger<SqlExecutionService> _logger;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new instance of <see cref="SqlExecutionService"/>.
    /// </summary>
    /// <param name="configuration">
    /// The host configuration, used to resolve the connection string by name from
    /// the <c>ConnectionStrings</c> section.
    /// </param>
    /// <param name="settings">
    /// Bound <see cref="SqlExecutionSettings"/> controlling timeout and row limits.
    /// </param>
    /// <param name="logger">Structured logger for this service.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required dependency is <see langword="null"/>.
    /// </exception>
    public SqlExecutionService(
        IConfiguration configuration,
        IOptions<SqlExecutionSettings> settings,
        ILogger<SqlExecutionService> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _configuration = configuration;
        _settings = settings.Value;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // ISqlExecutionService
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<dynamic>> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        cancellationToken.ThrowIfCancellationRequested();

        // ------------------------------------------------------------------
        // Resolve and validate connection string at call time (not startup) so
        // that misconfiguration surfaces as a clear operational error rather than
        // a cryptic NullReferenceException deep inside ADO.NET.
        // ------------------------------------------------------------------
        string connectionStringName = _settings.ConnectionStringName;
        string? connectionString = _configuration.GetConnectionString(connectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogError(
                "Connection string '{Name}' is missing or empty in configuration.",
                connectionStringName);

            throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' is not configured. " +
                "Ensure it is present under the 'ConnectionStrings' section in appsettings.");
        }

        int effectiveTimeout = _settings.CommandTimeoutSeconds < 0
            ? 30
            : _settings.CommandTimeoutSeconds;

        _logger.LogInformation(
            "Executing SQL query. Timeout={TimeoutSeconds}s, MaxRows={MaxRows}",
            effectiveTimeout,
            _settings.MaxRows);

        _logger.LogDebug("SQL statement: {Sql}", sql);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var connection = new SqlConnection(connectionString);

            // OpenAsync respects the cancellation token and will throw
            // OperationCanceledException if the token is signalled.
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                commandText: sql,
                commandTimeout: effectiveTimeout,
                cancellationToken: cancellationToken);

            IEnumerable<dynamic> rows = await connection
                .QueryAsync<dynamic>(command)
                .ConfigureAwait(false);

            // Materialise into a list so the connection can be closed before
            // the caller starts iterating the results.
            List<dynamic> results = rows.ToList();

            stopwatch.Stop();

            if (results.Count > _settings.MaxRows)
            {
                _logger.LogWarning(
                    "Query returned {ActualRows} rows which exceeds MaxRows={MaxRows}. " +
                    "Truncating to MaxRows. SQL={Sql}",
                    results.Count,
                    _settings.MaxRows,
                    sql);

                results = results.Take(_settings.MaxRows).ToList();
            }

            _logger.LogInformation(
                "SQL query executed successfully. RowCount={RowCount}, ElapsedMs={ElapsedMs}",
                results.Count,
                stopwatch.ElapsedMilliseconds);

            return results.AsReadOnly();
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(
                ex,
                "SQL query execution was cancelled after {ElapsedMs}ms.",
                stopwatch.ElapsedMilliseconds);

            throw;
        }
        catch (SqlException ex) when (ex.Number == -2 || ex.Class == 11)
        {
            // SQL Server error -2 / class 11 indicates a command timeout.
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "SQL command timed out after {TimeoutSeconds}s ({ElapsedMs}ms elapsed). " +
                "ErrorNumber={ErrorNumber}, SQL={Sql}",
                effectiveTimeout,
                stopwatch.ElapsedMilliseconds,
                ex.Number,
                sql);

            throw new TimeoutException(
                $"The SQL query did not complete within the configured timeout of {effectiveTimeout} second(s).",
                ex);
        }
        catch (SqlException ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "SQL Server returned an error after {ElapsedMs}ms. " +
                "ErrorNumber={ErrorNumber}, Severity={Severity}, State={State}, SQL={Sql}",
                stopwatch.ElapsedMilliseconds,
                ex.Number,
                ex.Class,
                ex.State,
                sql);

            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Unexpected error during SQL execution after {ElapsedMs}ms. SQL={Sql}",
                stopwatch.ElapsedMilliseconds,
                sql);

            throw;
        }
    }
}
