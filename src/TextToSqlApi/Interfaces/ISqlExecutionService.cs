namespace TextToSqlApi.Interfaces;

/// <summary>
/// Defines the contract for executing a validated SQL query against a SQL Server
/// database and returning the results as a dynamic collection.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are responsible for obtaining a database connection, executing
/// the provided SQL, and releasing all resources regardless of outcome.
/// </para>
/// <para>
/// Callers <b>must</b> validate the SQL string through
/// <see cref="ISqlValidator"/> before passing it to this service.  The service
/// itself does not re-validate the query; it trusts that the caller has already
/// ensured the statement is safe (SELECT-only).
/// </para>
/// </remarks>
public interface ISqlExecutionService
{
    /// <summary>
    /// Executes a pre-validated SQL query against the configured SQL Server database
    /// and returns all result rows as dynamic objects.
    /// </summary>
    /// <param name="sql">
    /// The SQL <c>SELECT</c> statement to execute. Must not be <see langword="null"/>
    /// or whitespace. The caller is responsible for ensuring this string has passed
    /// enterprise-safety validation prior to invocation.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that can be used to cancel the database operation. When cancelled,
    /// the method throws <see cref="OperationCanceledException"/> and any in-flight
    /// database command is aborted.
    /// </param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> that resolves to a read-only list of
    /// <see cref="dynamic"/> objects, each representing one result row.  An empty
    /// list is returned when the query produces no rows.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sql"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is signalled before or
    /// during execution.
    /// </exception>
    /// <exception cref="Microsoft.Data.SqlClient.SqlException">
    /// Propagated when the underlying SQL Server reports a database-level error
    /// (e.g., syntax error, permission denied).
    /// </exception>
    Task<IReadOnlyList<dynamic>> ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}
