using System.Text;
using TextToSqlApi.Interfaces;

namespace TextToSqlApi.Prompts;

/// <summary>
/// Builds the system and user prompt messages for Text-to-SQL translation.
/// All prompt templates are centralised here to keep them maintainable and testable
/// independently of the service orchestration layer.
/// </summary>
public sealed class TextToSqlPromptBuilder : IPromptBuilder
{
    /// <inheritdoc />
    public string BuildSystemPrompt(string sqlDialect)
    {
        return $"""
            You are an expert database engineer specialising in {sqlDialect}.
            Your sole responsibility is to convert natural-language questions into
            syntactically correct, safe, and efficient {sqlDialect} queries.

            Rules you MUST follow:
            1. Return ONLY the raw SQL statement — no markdown fences, no explanations.
            2. Never generate data-modifying statements (INSERT, UPDATE, DELETE, DROP, TRUNCATE,
               ALTER, CREATE) unless explicitly instructed.
            3. Always qualify column names with their table alias when the query spans
               multiple tables to avoid ambiguity.
            4. Apply the provided row limit using the dialect-appropriate clause
               (TOP for T-SQL, LIMIT for PostgreSQL/MySQL).
            5. Use parameterised placeholders (@param or :param) for any literal values
               that originate from user input to prevent SQL injection.
            6. If the question cannot be answered from the provided schema, respond with
               exactly: CANNOT_GENERATE_SQL
            7. Prefer CTEs (WITH clauses) over nested subqueries for readability.
            8. Do not include trailing semicolons unless the dialect requires them.
            """;
    }

    /// <inheritdoc />
    public string BuildUserPrompt(string naturalLanguageQuery, string? schemaContext, int maxRows)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(schemaContext))
        {
            sb.AppendLine("=== DATABASE SCHEMA ===");
            sb.AppendLine(schemaContext.Trim());
            sb.AppendLine("=== END SCHEMA ===");
            sb.AppendLine();
        }

        sb.AppendLine($"Row limit: {maxRows}");
        sb.AppendLine();
        sb.AppendLine("Question:");
        sb.Append(naturalLanguageQuery.Trim());

        return sb.ToString();
    }
}
