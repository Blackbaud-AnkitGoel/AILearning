using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;

namespace TextToSqlApi.Services;

/// <summary>
/// Reads SQL Server schema metadata (tables, columns, relationships) at runtime
/// and formats it as a concise text description for injection into AI prompts.
/// Results are cached in-memory to avoid repeated round-trips to the database on
/// every request.
/// </summary>
/// <remarks>
/// The service reads only from the <c>INFORMATION_SCHEMA</c> views, which are
/// available to any login with <c>VIEW DEFINITION</c> or <c>SELECT</c> permission.
/// No DDL or data is modified by this service.
/// </remarks>
public sealed class DatabaseSchemaService : IDatabaseSchemaService
{
    private const string CacheKey = "DatabaseSchema_v1";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly IConfiguration _configuration;
    private readonly SqlExecutionSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DatabaseSchemaService> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="DatabaseSchemaService"/>.
    /// </summary>
    /// <param name="configuration">Host configuration used to resolve the connection string.</param>
    /// <param name="settings">SQL execution settings (connection string name, timeout).</param>
    /// <param name="cache">In-memory cache used to store the formatted schema.</param>
    /// <param name="logger">Structured logger.</param>
    public DatabaseSchemaService(
        IConfiguration configuration,
        Microsoft.Extensions.Options.IOptions<SqlExecutionSettings> settings,
        IMemoryCache cache,
        ILogger<DatabaseSchemaService> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _configuration = configuration;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetSchemaContextAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out string? cached) && cached is not null)
        {
            _logger.LogDebug("Returning database schema from in-memory cache.");
            return cached;
        }

        _logger.LogInformation("Schema cache miss — reading schema from SQL Server.");

        string schema = await ReadSchemaFromDatabaseAsync(cancellationToken).ConfigureAwait(false);

        _cache.Set(CacheKey, schema, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1
        });

        _logger.LogInformation(
            "Database schema cached. SchemaLength={Length}, CacheDurationMinutes={Minutes}",
            schema.Length,
            CacheDuration.TotalMinutes);

        return schema;
    }

    /// <inheritdoc />
    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
        _logger.LogInformation("Database schema cache invalidated.");
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<string> ReadSchemaFromDatabaseAsync(CancellationToken cancellationToken)
    {
        string? connectionString = _configuration.GetConnectionString(_settings.ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{_settings.ConnectionStringName}' is not configured.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // ── Read columns ──────────────────────────────────────────────────
        const string columnsSql = @"
SELECT
    c.TABLE_SCHEMA   AS TableSchema,
    c.TABLE_NAME     AS TableName,
    c.COLUMN_NAME    AS ColumnName,
    c.DATA_TYPE      AS DataType,
    c.IS_NULLABLE    AS IsNullable,
    c.CHARACTER_MAXIMUM_LENGTH AS MaxLength
FROM INFORMATION_SCHEMA.COLUMNS c
INNER JOIN INFORMATION_SCHEMA.TABLES t
    ON  t.TABLE_SCHEMA = c.TABLE_SCHEMA
    AND t.TABLE_NAME   = c.TABLE_NAME
    AND t.TABLE_TYPE   = 'BASE TABLE'
ORDER BY
    c.TABLE_SCHEMA,
    c.TABLE_NAME,
    c.ORDINAL_POSITION;";

        var columns = (await connection.QueryAsync(new CommandDefinition(
            columnsSql,
            commandTimeout: _settings.CommandTimeoutSeconds,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

        // ── Read foreign keys ─────────────────────────────────────────────
        const string fkSql = @"
SELECT
    fk.name                                     AS ConstraintName,
    OBJECT_SCHEMA_NAME(fk.parent_object_id)     AS ParentSchema,
    OBJECT_NAME(fk.parent_object_id)            AS ParentTable,
    COL_NAME(fkc.parent_object_id, fkc.parent_column_id)       AS ParentColumn,
    OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ReferencedSchema,
    OBJECT_NAME(fk.referenced_object_id)        AS ReferencedTable,
    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ReferencedColumn
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc
    ON fkc.constraint_object_id = fk.object_id
ORDER BY ParentTable, ParentColumn;";

        var foreignKeys = (await connection.QueryAsync(new CommandDefinition(
            fkSql,
            commandTimeout: _settings.CommandTimeoutSeconds,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

        return FormatSchema(columns, foreignKeys);
    }

    private static string FormatSchema(
        IReadOnlyList<dynamic> columns,
        IReadOnlyList<dynamic> foreignKeys)
    {
        if (columns.Count == 0)
        {
            return "-- No user tables found in the database.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("-- DATABASE SCHEMA");
        sb.AppendLine("-- Tables and columns:");
        sb.AppendLine();

        // Group columns by schema+table
        var tables = columns
            .GroupBy(c => $"{c.TableSchema}.{c.TableName}")
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (tableKey, tableColumns) in tables)
        {
            sb.AppendLine($"TABLE {tableKey} (");

            foreach (var col in tableColumns)
            {
                string nullable = col.IsNullable == "YES" ? "NULL" : "NOT NULL";
                string typeDisplay = col.MaxLength is not null and not -1
                    ? $"{col.DataType}({col.MaxLength})"
                    : (string)col.DataType;

                sb.AppendLine($"    {col.ColumnName} {typeDisplay} {nullable},");
            }

            sb.AppendLine(");");
            sb.AppendLine();
        }

        if (foreignKeys.Count > 0)
        {
            sb.AppendLine("-- Relationships (foreign keys):");
            foreach (var fk in foreignKeys)
            {
                sb.AppendLine(
                    $"-- {fk.ParentSchema}.{fk.ParentTable}.{fk.ParentColumn} " +
                    $"-> {fk.ReferencedSchema}.{fk.ReferencedTable}.{fk.ReferencedColumn}");
            }
        }

        return sb.ToString();
    }
}
