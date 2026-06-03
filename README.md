# Enterprise AI Text-to-SQL API

A production-ready **.NET 9 ASP.NET Core Web API** that translates natural-language questions into SQL statements using **Semantic Kernel** with an **OpenAI-compatible endpoint** (GitHub Models, openai.com, or Azure OpenAI).

---

## Solution Structure

```
TextToSqlApi/
├── TextToSqlApi.slnx
└── src/
    └── TextToSqlApi/
        ├── Controllers/          # API surface — HTTP request handling
        ├── Services/             # Core business logic + AI orchestration
        ├── Prompts/              # Prompt templates (system + user)
        ├── Validators/           # Input validation rules
        ├── Models/
        │   ├── Requests/         # Inbound DTOs
        │   └── Responses/        # Outbound DTOs + error envelope
        ├── Interfaces/           # Contracts (ITextToSqlService, IPromptBuilder, IRequestValidator)
        ├── Extensions/           # IServiceCollection + IApplicationBuilder helpers
        ├── Program.cs            # Host bootstrap
        ├── appsettings.json
        └── appsettings.Development.json
```

---

## Quick Start

### Prerequisites

| Tool       | Version |
|------------|---------|
| .NET SDK   | 9.x     |
| GitHub Personal Access Token | classic or fine-grained (free tier available) |

### 1 — Configure the OpenAI-compatible endpoint

Edit `appsettings.Development.json` or use user-secrets / environment variables:

```json
"OpenAI": {
  "Endpoint":        "https://models.inference.ai.azure.com",
  "ApiKey":          "<your-github-pat>",
  "DeploymentName":  "gpt-4o",
  "EmbeddingModelId": ""
}
```

**Endpoint options**

| Provider | Endpoint |
|---|---|
| GitHub Models (default) | `https://models.inference.ai.azure.com` |
| openai.com | `https://api.openai.com/v1` |
| Azure OpenAI | `https://<resource>.openai.azure.com/` |

> In production, store the API key in **Azure Key Vault** and reference it via the `AzureKeyVaultConfigurationProvider`.

### 2 — Run

```bash
dotnet run --project src/TextToSqlApi
```

Swagger UI: `https://localhost:<port>/swagger`  
Health check: `https://localhost:<port>/health`

### 3 — Sample request

```http
POST /api/v1/texttosql/translate
Content-Type: application/json

{
  "naturalLanguageQuery": "Show me all customers who placed more than 5 orders last month",
  "schemaContext": "CREATE TABLE Customers (Id INT, Name NVARCHAR(100)); CREATE TABLE Orders (Id INT, CustomerId INT, OrderDate DATE);",
  "sqlDialect": "T-SQL",
  "maxRows": 50
}
```

---

## Key Design Decisions

| Area | Decision |
|------|----------|
| AI orchestration | **Semantic Kernel** — provider-agnostic abstraction; swap backend by changing one line in `ServiceCollectionExtensions` |
| Endpoint | **OpenAI-compatible REST surface** — GitHub Models by default; works unchanged with openai.com or Azure OpenAI |
| Logging | **Serilog** with structured JSON output; rolling file sink for retention |
| Validation | Custom `IRequestValidator` — no runtime reflection, deterministic, easily unit-testable |
| Error handling | Global middleware returns RFC 7807–style JSON; detail only exposed in Development |
| Prompt management | Centralized in `TextToSqlPromptBuilder`; system rules prevent data-modifying SQL |
| DI lifetime | `Kernel` and `IChatCompletionService` are **singletons** — `Kernel` is immutable after `Build()` and safe for concurrent use |

---

## Extending the Project

- **Add a new dialect** → update `TextToSqlRequestValidator.SupportedDialects` and `TextToSqlPromptBuilder.BuildSystemPrompt`.
- **Swap AI backend** → change `Endpoint`, `ApiKey`, and `DeploymentName` in `appsettings.json`; no code changes required.
- **Enable embeddings** → set `EmbeddingModelId` in `appsettings.json` and uncomment the embeddings block in `ServiceCollectionExtensions.AddSemanticKernel`.
- **Add telemetry** → inject `IKernelFilter` from Semantic Kernel for token cost tracking.
- **Authentication** → add `builder.Services.AddAuthentication()` with Azure AD / MSAL and protect routes with `[Authorize]`.
