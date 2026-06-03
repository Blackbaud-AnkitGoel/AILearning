using System.Text.RegularExpressions;
using TextToSqlApi.Interfaces;
using TextToSqlApi.Models;

namespace TextToSqlApi.Validators;

/// <summary>
/// Enterprise-safe SQL validator that enforces a strict SELECT-only policy on
/// AI-generated SQL before it is executed against the database.
/// </summary>
/// <remarks>
/// <para><strong>Validation pipeline (in order):</strong></para>
/// <list type="number">
///   <item><description>Null / empty guard.</description></item>
///   <item><description>Strip <c>--</c> and <c>/* */</c> comments to prevent comment-injection bypass.</description></item>
///   <item><description>Reject multiple statements (semicolons not inside string literals).</description></item>
///   <item><description>Reject forbidden DML/DDL keywords (DELETE, UPDATE, INSERT, DROP, ALTER, EXEC, EXECUTE, MERGE, TRUNCATE).</description></item>
///   <item><description>Reject cross-database / schema-access patterns (<c>USE database</c>, <c>xp_</c>, <c>sp_</c>).</description></item>
///   <item><description>Enforce SELECT-only allowlist (statement must start with SELECT or WITH).</description></item>
///   <item><description>Enforce complexity guard (nesting depth / length limit).</description></item>
///   <item><description>Inject <c>TOP 100</c> if no row-limiter is present.</description></item>
/// </list>
/// <para>
/// All <see cref="Regex"/> patterns are pre-compiled and executed with a 250 ms
/// timeout to guard against ReDoS attacks.
/// </para>
/// </remarks>
public sealed class SqlValidator : ISqlValidator
{
    // -------------------------------------------------------------------------
    // Configuration
    // -------------------------------------------------------------------------

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Hard limit on SQL string length to prevent DoS via enormous inputs.</summary>
    private const int MaxSqlLength = 8_000;

    /// <summary>Maximum subquery nesting depth (counted by parenthesis depth).</summary>
    private const int MaxNestingDepth = 10;

    // -------------------------------------------------------------------------
    // Pre-compiled regex patterns
    // -------------------------------------------------------------------------

    private static readonly Regex SingleLineCommentRegex = new(
        @"--[^\r\n]*",
        RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex BlockCommentRegex = new(
        @"/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ForbiddenKeywordsRegex = new(
        @"\b(DELETE|UPDATE|INSERT|DROP|ALTER|EXEC|EXECUTE|MERGE|TRUNCATE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// Detects a semicolon that sits outside single-quoted string literals.
    /// Strategy: remove all single-quoted strings first, then check for any semicolon.
    /// </summary>
    private static readonly Regex StripStringLiteralsRegex = new(
        @"'(?:[^']|'')*'",
        RegexOptions.Singleline | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex StartsWithSelectRegex = new(
        @"^\s*(SELECT|WITH)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    /// <summary>Detects <c>USE database</c> cross-database access attempts.</summary>
    private static readonly Regex UseDatabaseRegex = new(
        @"\bUSE\s+\w+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    /// <summary>Detects dangerous system stored-procedure prefixes.</summary>
    private static readonly Regex SystemProcedureRegex = new(
        @"\b(xp_|sp_)\w+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    /// <summary>Detects presence of TOP or FETCH NEXT row-limiting clauses.</summary>
    private static readonly Regex HasRowLimiterRegex = new(
        @"\b(TOP\s*\d+|FETCH\s+NEXT|LIMIT\s+\d+|ROWNUM\s*[<=])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    /// <summary>Injection point for TOP – matches the SELECT keyword to insert TOP after it.</summary>
    private static readonly Regex SelectKeywordRegex = new(
        @"\bSELECT\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    // -------------------------------------------------------------------------
    // ISqlValidator
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public SqlValidationResult Validate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return SqlValidationResult.Failure("SQL query cannot be null or empty.");

        if (sql.Length > MaxSqlLength)
            return SqlValidationResult.Failure(
                $"SQL query exceeds the maximum allowed length of {MaxSqlLength} characters.");

        // Strip a single trailing semicolon — AI models commonly append one,
        // but it is harmless on a single statement and should not be rejected.
        sql = sql.TrimEnd().TrimEnd(';').TrimEnd();

        string sanitized;
        try
        {
            // Step 1 – Strip comments before any keyword checks.
            sanitized = SingleLineCommentRegex.Replace(sql, " ");
            sanitized = BlockCommentRegex.Replace(sanitized, " ");
        }
        catch (RegexMatchTimeoutException)
        {
            return SqlValidationResult.Failure("SQL validation timed out. The input may be malformed.");
        }

        try
        {
            // Step 2 – Reject multiple statements (semicolons in the middle, outside string literals).
            // Strip string literals first so a semicolon inside 'hello; world' is ignored.
            string noStrings = StripStringLiteralsRegex.Replace(sanitized, "''");
            if (noStrings.Contains(';'))
                return SqlValidationResult.Failure(
                    "SQL must be a single statement. Semicolon-chained queries are not permitted.");

            // Step 3 – Reject forbidden DML/DDL keywords.
            Match forbidden = ForbiddenKeywordsRegex.Match(sanitized);
            if (forbidden.Success)
                return SqlValidationResult.Failure(
                    $"SQL contains a forbidden keyword: '{forbidden.Value.ToUpperInvariant()}'. " +
                    "Only SELECT statements are permitted.");

            // Step 4 – Reject cross-database / system procedure access.
            if (UseDatabaseRegex.IsMatch(sanitized))
                return SqlValidationResult.Failure(
                    "USE <database> statements are not permitted.");

            if (SystemProcedureRegex.IsMatch(sanitized))
                return SqlValidationResult.Failure(
                    "System stored procedure calls (xp_, sp_) are not permitted.");

            // Step 5 – SELECT-only allowlist.
            if (!StartsWithSelectRegex.IsMatch(sanitized))
                return SqlValidationResult.Failure(
                    "Only SELECT statements are permitted. " +
                    "The query must begin with SELECT or WITH (for common table expressions).");
        }
        catch (RegexMatchTimeoutException)
        {
            return SqlValidationResult.Failure("SQL validation timed out. The input may be malformed.");
        }

        // Step 6 – Complexity guard (parenthesis nesting depth).
        int depth = 0, maxDepth = 0;
        foreach (char ch in sanitized)
        {
            if (ch == '(') { depth++; if (depth > maxDepth) maxDepth = depth; }
            else if (ch == ')') { depth--; }
        }

        if (maxDepth > MaxNestingDepth)
            return SqlValidationResult.Failure(
                $"SQL query is too complex (nesting depth {maxDepth} exceeds limit of {MaxNestingDepth}). " +
                "Please simplify the query.");

        // Step 7 – Inject TOP 100 when no row-limiter clause is present.
        // This is a defence-in-depth safeguard; the caller may also enforce MaxRows.
        try
        {
            if (!HasRowLimiterRegex.IsMatch(sanitized))
            {
                sql = SelectKeywordRegex.Replace(sql, "SELECT TOP 100", count: 1);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Non-fatal – return original SQL without the TOP injection.
        }

        return SqlValidationResult.Success(sql);
    }
}
