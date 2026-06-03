# Text-to-SQL AI API

An enterprise-grade ASP.NET Core 9 Web API that translates natural-language questions into safe SQL queries using **GitHub Models (GPT-4o via Semantic Kernel)**, executes them against SQL Server, and returns both the raw data and a business-friendly AI-generated summary.

---

## Architecture

```
HTTP Request
     │
     ▼
┌──────────────────────────────────────────────────┐
│  GlobalExceptionMiddleware  (ProblemDetails RFC7807) │
└──────────────────────────┬───────────────────────┘
                           │
              ┌────────────▼────────────┐
              │    QueryController      │  POST /api/query
              └────────────┬────────────┘
                           │
            ┌──────────────▼───────────────┐
            │  4-Step Query Pipeline        │
            │                              │
            │  1. ITextToSqlService        │  ← Semantic Kernel + GitHub Models
            │     (via ResilientDecorator) │  ← Polly retry + timeout
            │                              │
            │  2. ISqlValidator            │  ← 7-step safety validation
            │                              │
            │  3. ISqlExecutionService     │  ← Dapper + SQL Server
            │                              │
            │  4. IResultSummaryService    │  ← Semantic Kernel summarisation
            └──────────────────────────────┘
```

### Key Services

| Service | Responsibility |
|---------|----------------|
| `TextToSqlService` | Prompts GPT-4o to generate SQL from natural language |
| `ResilientTextToSqlService` | Polly decorator — exponential backoff retry + total timeout |
| `SqlValidator` | 7-step enterprise SQL safety validation (SELECT-only, TOP injection) |
| `SqlExecutionService` | Executes validated SQL against SQL Server via Dapper |
| `DatabaseSchemaService` | Reads live schema from `INFORMATION_SCHEMA`, cached 30 min |
| `ResultSummaryService` | Summarises query results in business-friendly English |

---

## Tech Stack

| Concern | Technology |
|---------|-----------|
| Framework | ASP.NET Core 9 Web API |
| AI / LLM | Semantic Kernel 1.21.1 + GitHub Models (GPT-4o) |
| Data Access | Dapper 2.1.35 + Microsoft.Data.SqlClient 5.2.2 |
| Resilience | Polly 8.x — exponential backoff, total timeout |
| Logging | Serilog — structured JSON logs, daily rolling files |
| Documentation | Swashbuckle / Swagger UI with XML comments |
| Validation | Custom `SqlValidator` (7-step SELECT-only policy) |
| Caching | `IMemoryCache` — schema cached 30 minutes |

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- SQL Server (local or remote) with a database to query
- [GitHub Personal Access Token](https://github.com/settings/tokens) with `models:read` permission (for GitHub Models)

---

## Quick Start

### 1. Clone and navigate

```bash
git clone <repo-url>
cd TextToSqlApi/src/TextToSqlApi
```

### 2. Configure settings

Edit `appsettings.json` (or use environment variables / user secrets):

```json
{
  "OpenAI": {
    "Endpoint": "https://models.inference.ai.azure.com",
    "DeploymentName": "gpt-4o",
    "ApiKey": "<YOUR_GITHUB_PAT>"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=YourDatabase;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

**For development (recommended — keeps secrets out of source control):**

```bash
dotnet user-secrets set "OpenAI:ApiKey" "<YOUR_GITHUB_PAT>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<YOUR_CONNECTION_STRING>"
```

### 3. Run

```bash
dotnet run
```

Navigate to `https://localhost:5001/swagger` for the interactive API documentation.

---

## API Endpoints

### `POST /api/query`

End-to-end pipeline: natural language → SQL → execute → summarise.

**Request:**
```json
{
  "question": "Show me the top 10 customers by total order value this year",
  "sqlDialect": "T-SQL",
  "maxRows": 100
}
```

**Response (200 OK):**
```json
{
  "question": "Show me the top 10 customers by total order value this year",
  "sqlQuery": "SELECT TOP 100 c.CustomerName, SUM(o.TotalAmount) AS TotalOrderValue FROM Customers c JOIN Orders o ON c.CustomerId = o.CustomerId WHERE YEAR(o.OrderDate) = YEAR(GETDATE()) GROUP BY c.CustomerName ORDER BY TotalOrderValue DESC",
  "summary": "The top 10 customers by order value this year are led by Acme Corp with £450,000, followed by...",
  "data": [...],
  "rowCount": 10,
  "correlationId": "abc123",
  "generatedAt": "2024-01-15T10:30:00Z"
}
```

### `POST /api/texttosql/translate`

SQL generation only (no execution or summarisation).

**Request:**
```json
{
  "naturalLanguageQuery": "How many orders were placed last month?",
  "sqlDialect": "T-SQL",
  "maxRows": 100
}
```

**Response (200 OK):**
```json
{
  "generatedSql": "SELECT TOP 100 COUNT(*) AS OrderCount FROM Orders WHERE ...",
  "originalQuery": "How many orders were placed last month?",
  "confidenceScore": 0.9,
  "sqlDialect": "T-SQL"
}
```

### `GET /api/texttosql/health`

Lightweight liveness check.

---

## SQL Safety Validation

The `SqlValidator` enforces a strict 7-step pipeline before any SQL touches the database:

1. **Null/empty guard** — rejects blank input
2. **Length check** — max 8,000 characters
3. **Comment stripping** — removes `--` and `/* */` comments to prevent injection bypass
4. **Semicolon rejection** — blocks multi-statement queries
5. **Forbidden keyword check** — blocks DELETE, UPDATE, INSERT, DROP, ALTER, EXEC, EXECUTE, MERGE, TRUNCATE
6. **System access block** — blocks `USE <database>`, `xp_*`, `sp_*` procedures
7. **SELECT-only allowlist** — statement must start with `SELECT` or `WITH`
8. **Complexity guard** — max nesting depth of 10
9. **TOP injection** — automatically adds `SELECT TOP 100` if no row limiter is present

---

## Switching AI Model Providers

The application uses Semantic Kernel's `IChatCompletionService` abstraction, which makes swapping AI providers straightforward. The provider is configured entirely via `appsettings.json`.

### Config-only change (URL + Key only)

These providers expose an **OpenAI-compatible API** — no code changes needed, just update `Endpoint`, `DeploymentName`, and `ApiKey`:

#### GitHub Models (default — free, rate-limited)

```json
"OpenAI": {
  "Endpoint": "https://models.inference.ai.azure.com",
  "DeploymentName": "gpt-4o",
  "ApiKey": "<GITHUB_PAT>"
}
```

Available `DeploymentName` values on GitHub Models:

| Model | `DeploymentName` | Best for |
|-------|-----------------|----------|
| GPT-4o | `gpt-4o` | Best SQL quality (default) |
| GPT-4o mini | `gpt-4o-mini` | Faster, lower cost |
| GPT-4.1 | `gpt-4.1` | Latest GPT-4 series |
| GPT-4.1 mini | `gpt-4.1-mini` | Fast + capable |
| GPT-4.1 nano | `gpt-4.1-nano` | Fastest, lightweight |
| o1-mini | `o1-mini` | Complex reasoning |
| o3-mini | `o3-mini` | Advanced reasoning |
| Llama 3.3 70B | `meta-llama-3.3-70b-instruct` | Open-source option |
| Mistral Large | `mistral-large-2411` | European data residency |
| DeepSeek R1 | `deepseek-r1` | Reasoning tasks |
| Phi-4 | `Phi-4` | Microsoft small model |

#### Azure OpenAI

```json
"OpenAI": {
  "Endpoint": "https://YOUR-RESOURCE.openai.azure.com",
  "DeploymentName": "your-deployment-name",
  "ApiKey": "<AZURE_OPENAI_KEY>"
}
```

#### Google Gemini

```json
"OpenAI": {
  "Endpoint": "https://generativelanguage.googleapis.com/v1beta/openai",
  "DeploymentName": "gemini-2.0-flash",
  "ApiKey": "<GOOGLE_API_KEY>"
}
```

#### Groq (very fast inference)

```json
"OpenAI": {
  "Endpoint": "https://api.groq.com/openai/v1",
  "DeploymentName": "llama-3.3-70b-versatile",
  "ApiKey": "<GROQ_API_KEY>"
}
```

#### Mistral AI

```json
"OpenAI": {
  "Endpoint": "https://api.mistral.ai/v1",
  "DeploymentName": "mistral-large-latest",
  "ApiKey": "<MISTRAL_API_KEY>"
}
```

#### Ollama (local — fully private, no internet required)

```json
"OpenAI": {
  "Endpoint": "http://localhost:11434/v1",
  "DeploymentName": "llama3.3",
  "ApiKey": "ollama"
}
```

#### Together AI / Fireworks AI

```json
"OpenAI": {
  "Endpoint": "https://api.together.xyz/v1",
  "DeploymentName": "meta-llama/Llama-3-70b-chat-hf",
  "ApiKey": "<TOGETHER_API_KEY>"
}
```

---

### One-line code change required

These providers use a **proprietary API format** — not OpenAI-compatible. They require replacing one line in [`ServiceCollectionExtensions.cs`](src/TextToSqlApi/Extensions/ServiceCollectionExtensions.cs) and installing the relevant Semantic Kernel connector NuGet package:

| Provider | NuGet Package | Code change |
|----------|--------------|-------------|
| **AWS Bedrock** (Claude, Titan, Llama) | `Microsoft.SemanticKernel.Connectors.Amazon` | `kernelBuilder.AddBedrockChatCompletion(modelId, amazonBedrockClient)` |
| **Anthropic Claude** (direct) | `Microsoft.SemanticKernel.Connectors.Anthropic` | `kernelBuilder.AddAnthropicChatCompletion(modelId, apiKey)` |
| **Google Vertex AI** | `Microsoft.SemanticKernel.Connectors.Google` | `kernelBuilder.AddVertexAIChatCompletion(modelId, ...)` |
| **Cohere** | `Microsoft.SemanticKernel.Connectors.Cohere` | `kernelBuilder.AddCohereChatCompletion(modelId, apiKey)` |

All other application code (`TextToSqlService`, `ResultSummaryService`, controllers, etc.) remains completely unchanged because they only depend on `IChatCompletionService`.

---

## Configuration Reference

### `appsettings.json` Sections

```json
{
  "OpenAI": {
    "Endpoint": "https://models.inference.ai.azure.com",
    "DeploymentName": "gpt-4o",
    "ApiKey": "",
    "MaxTokens": 1000,
    "Temperature": 0.0
  },
  "ConnectionStrings": {
    "DefaultConnection": "..."
  },
  "SqlExecution": {
    "ConnectionStringName": "DefaultConnection",
    "CommandTimeoutSeconds": 30,
    "MaxRows": 1000
  },
  "Resilience": {
    "MaxRetryAttempts": 3,
    "BaseDelaySeconds": 2,
    "TotalTimeoutSeconds": 60
  },
  "Swagger": {
    "AlwaysEnable": true
  }
}
```

---

## Logging

Structured logs are written to:
- **Console** — with correlation ID in every line
- **`logs/text-to-sql-YYYYMMDD.log`** — rolling daily, 7-day retention

Every request carries a `CorrelationId` (from the `X-Correlation-Id` header or auto-generated) that is included in all log entries for the lifetime of the request.

---

## Security Considerations

- **SQL injection** — Mitigated by the `SqlValidator` SELECT-only policy; no user-supplied values are ever interpolated into SQL
- **Secrets** — Use `.NET User Secrets` or environment variables; never commit API keys
- **OWASP A03 (Injection)** — AI-generated SQL is validated before execution; no raw user input reaches the database
- **OWASP A05 (Misconfiguration)** — Swagger is configurable via `Swagger:AlwaysEnable`; disable in production if not needed
- **ReDoS** — All regex patterns in `SqlValidator` run with a 250 ms timeout
- **Row limiting** — `TOP 100` is injected if absent; `MaxRows` enforced at the execution layer

---

## Project Structure

```
src/TextToSqlApi/
├── Controllers/
│   ├── QueryController.cs          # End-to-end pipeline endpoint
│   └── TextToSqlController.cs      # SQL generation + health endpoints
├── Extensions/
│   ├── ServiceCollectionExtensions.cs  # DI registration
│   └── ApplicationBuilderExtensions.cs # Middleware pipeline
├── Interfaces/                     # Contracts for all services
├── Middleware/
│   └── GlobalExceptionMiddleware.cs    # RFC 7807 ProblemDetails handler
├── Models/
│   ├── Requests/                   # Request DTOs
│   └── Responses/                  # Response DTOs
├── Prompts/
│   ├── sql-generation.txt          # AI prompt template for SQL generation
│   ├── result-summary.txt          # AI prompt template for summaries
│   └── TextToSqlPromptBuilder.cs   # Prompt construction logic
├── Services/
│   ├── TextToSqlService.cs         # Core AI translation service
│   ├── ResilientTextToSqlService.cs # Polly resilience decorator
│   ├── SqlExecutionService.cs      # Dapper SQL execution
│   ├── DatabaseSchemaService.cs    # Live schema reader + cache
│   └── ResultSummaryService.cs     # AI result summarisation
├── Validators/
│   ├── SqlValidator.cs             # 7-step SQL safety validator
│   └── TextToSqlRequestValidator.cs # Request DTO validator
├── Program.cs
├── appsettings.json
└── TextToSqlApi.csproj
```

---

## Future Enhancements

- **Authentication** — Add JWT bearer or API-key middleware
- **Schema auto-injection** — Wire `DatabaseSchemaService` into `QueryController` to automatically inject live schema into the SQL generation prompt
- **Streaming responses** — Support `IAsyncEnumerable` streaming for large result sets
- **Query history** — Persist generated SQL + results for audit and replay
- **Rate limiting** — Add `AspNetCoreRateLimiting` per client/IP
- **Health checks** — Add `/health` using `Microsoft.Extensions.Diagnostics.HealthChecks` with SQL Server probe
