namespace TextToSqlApi.Interfaces;

/// <summary>
/// Builds the system and user prompt messages that are sent to the AI model.
/// </summary>
public interface IPromptBuilder
{
    /// <summary>
    /// Constructs the system prompt that establishes the AI model's persona and behaviour rules.
    /// </summary>
    /// <param name="sqlDialect">Target SQL dialect (e.g., T-SQL, PostgreSQL).</param>
    /// <returns>The rendered system prompt string.</returns>
    string BuildSystemPrompt(string sqlDialect);

    /// <summary>
    /// Constructs the user prompt from the natural-language query and optional schema context.
    /// </summary>
    /// <param name="naturalLanguageQuery">The raw user question.</param>
    /// <param name="schemaContext">Optional DDL or JSON schema to include.</param>
    /// <param name="maxRows">Row limit to embed in the prompt instruction.</param>
    /// <returns>The rendered user prompt string.</returns>
    string BuildUserPrompt(string naturalLanguageQuery, string schemaContext, int maxRows);
}
