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
            You are an expert {sqlDialect} database engineer.
            Your sole task is to translate natural-language questions into valid {sqlDialect} SELECT statements.

            Rules:
            - Output ONLY the SQL statement — no explanation, no markdown fences, no commentary.
            - Always use SELECT statements. Never use INSERT, UPDATE, DELETE, DROP, ALTER, EXEC, TRUNCATE, or MERGE.
            - Use only the tables and columns provided in the schema context.
            - Always qualify ambiguous column names with their table alias.
            - Prefer JOINs over subqueries where appropriate.
            - If the question cannot be answered with the available schema, respond with:
              SELECT 'Unable to generate SQL: insufficient schema information' AS ErrorMessage
            """;
    }

    /// <inheritdoc />
    public string BuildUserPrompt(string naturalLanguageQuery, string schemaContext, int maxRows)
    {
        var schemaSection = string.IsNullOrWhiteSpace(schemaContext)
            ? string.Empty
            : $"""

               DATABASE SCHEMA:
               {schemaContext}

               """;

        return $"""
            {schemaSection}
            QUESTION: {naturalLanguageQuery}

            Limit results to {maxRows} rows using TOP {maxRows}.
            Return only the SQL statement.
            """;
    }
}
