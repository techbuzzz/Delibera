<div align="center">

<img src="img/delibera-horizontal-1920x480.png" alt="Delibera" width="640">

# Delibera

### ⚖️ Thoughtful AI Decisions

**Collective decision making through structured AI deliberation — with RAG, pgvector, Knowledge Keeper, Chairman, 🔥 Context Compression, 💉 Dependency Injection & 📋 Execution Logging**

[![NuGet](https://img.shields.io/nuget/v/Delibera.Core.svg)](https://www.nuget.org/packages/Delibera.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-10B981.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-1F2937.svg)](https://dotnet.microsoft.com)
[![C# 14](https://img.shields.io/badge/C%23-14.0-239120.svg)](https://learn.microsoft.com/dotnet/csharp/)

</div>

---

## 📖 Overview

**Delibera** is a C# / .NET 10 framework that orchestrates **multi-model deliberations** between LLMs.
Multiple AI models reason through a question across structured rounds, critique each other's
answers, and a **Chairman** weighs the arguments to synthesise a balanced final verdict — enriched by a
**Knowledge Keeper** backed by **Qdrant** or **PostgreSQL/pgvector** (RAG), with **intelligent
context compression** to minimise token usage.

The name comes from *deliberation* — the careful weighing of evidence and viewpoints before
reaching a decision. Delibera brings that discipline to AI, helping teams reach **thoughtful,
well-reasoned outcomes** rather than single-model guesses.

---

## ✨ Key Features

| Feature                       | Description                                                                           |
| ----------------------------- | ------------------------------------------------------------------------------------- |
| **🏛️ Multi-Model Councils**  | Orchestrate any number of LLM participants across structured debate rounds            |
| **⚖️ Chairman Synthesis**     | A dedicated moderator opens, regulates, and synthesises the final verdict             |
| **📚 Knowledge Keeper (RAG)** | Per-round semantic retrieval with structured, cited responses                         |
| **🐘 Qdrant + pgvector**      | Pluggable vector stores — use a dedicated DB or your existing PostgreSQL              |
| **🗜️ Context Compression**   | 4 strategies (Semantic, Deduplication, Summarization, Hybrid) save 30–70% of tokens   |
| **💉 Dependency Injection**   | `AddDelibera()` extension for `IServiceCollection` with full options binding          |
| **📋 Execution Logging**      | `ExecutionLog` model with `ExecutionLogLevel` — Chairman, KK, Compression & participant events |
| **📝 M.E.Logging**           | Inject your own `ILogger`/`ILoggerFactory` — every debate event is forwarded to the host's logging pipeline |
| **🌐 Response Language**     | Force every model (participants, Chairman, KK, Operator) to answer in a specific language |
| **⚡ Parallel Operator**     | `[[OPERATOR: …]]` tasks within a round run in parallel, bounded by `MaxDegreeOfParallelism` |
| **📁 Separate File Output**   | Export `result.md`, `statistics.md`, and `logs.md` independently                      |
| **🔌 Interface-First**        | Clean abstractions for providers, factories, builders and executors                   |
| **🧱 Modern C# 12**           | File-scoped namespaces, records, init-only properties, global usings                  |

---

## 📑 Table of Contents

- [Quick Start](#-quick-start)
  - [Prerequisites & Models](#prerequisites--models)
  - [Installation](#installation)
  - [Minimal Example](#minimal-example)
  - [Run](#run)
- [Dependency Injection](#-dependency-injection)
- [Context Compression](#-context-compression)
- [RAG Integration](#-rag-integration)
- [Debate Strategies](#-debate-strategies)
- [Output Files Structure](#-output-files-structure)
- [ConsoleApp Examples](#-consoleapp-examples)
- [Installation & Build](#-installation--build)
- [Architecture](#-architecture)
- [Contributing](#-contributing)
- [License](#-license)

---

## 🚀 Quick Start

### Prerequisites & Models

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A running [Ollama](https://ollama.com) instance — local (`ollama serve`) or
  [Ollama Cloud](https://ollama.com/cloud) (just an API key, no install).
- The minimal set of models listed below.

#### 🟢 Minimal — for a tiny debate (≈ 2 GB total)

Suitable for smoke-testing, low-end hardware, and quick CLI runs.

| Purpose         | Model               | Size  | Pull command                       |
| --------------- | ------------------- | ----- | ---------------------------------- |
| Council member  | `llama3.2:1b`       | 1.3 GB | `ollama pull llama3.2:1b`        |
| Council member  | `qwen2.5:1.5b`      | 1.1 GB | `ollama pull qwen2.5:1.5b`       |
| Embeddings (RAG)| `nomic-embed-text`  | 274 MB | `ollama pull nomic-embed-text`   |

#### 🟡 Standard — recommended for most use cases (≈ 7 GB total)

Good reasoning quality with low latency. **This is the default set used throughout the README.**

| Purpose         | Model                | Size    | Pull command                        |
| --------------- | -------------------- | ------- | ----------------------------------- |
| Council member  | `llama3.2:3b`        | 2.0 GB  | `ollama pull llama3.2:3b`         |
| Council member  | `qwen2.5:7b`         | 4.7 GB  | `ollama pull qwen2.5:7b`          |
| Embeddings (RAG)| `nomic-embed-text`   | 274 MB  | `ollama pull nomic-embed-text`    |

#### 🔴 High-Performance — for production-grade deliberations (≈ 30+ GB)

Heavier local models, recommended on GPUs with ≥ 24 GB VRAM or on Ollama Cloud.

| Purpose         | Model                       | Size      | Pull command                                  |
| --------------- | --------------------------- | --------- | --------------------------------------------- |
| Council member  | `llama3.1:8b`               | 4.9 GB    | `ollama pull llama3.1:8b`                    |
| Council member  | `qwen2.5:14b`               | 9.0 GB    | `ollama pull qwen2.5:14b`                    |
| Council member  | `mistral:7b`                | 4.4 GB    | `ollama pull mistral:7b`                     |
| Chairman        | `qwen2.5:14b` *(or larger)* | 9.0 GB    | `ollama pull qwen2.5:14b`                    |
| Embeddings (RAG)| `nomic-embed-text`          | 274 MB    | `ollama pull nomic-embed-text`               |

> 💡 **Ollama Cloud** has the same model names but doesn't require local disk space — you only
> need an API key. See the [Configuration section](#-dependency-injection) for setting it.

### Installation

```bash
dotnet add package Delibera.Core
```

### Minimal Example

```csharp
using Delibera.Core.Council;
using Delibera.Core.Providers;

using var factory = new ProviderFactory();
var ollama = factory.CreateOllama("http://localhost:11434");

var result = await new CouncilBuilder()
    .AddMember("llama3.2:3b", ollama, "Analyst")
    .AddMember("qwen2.5:7b", ollama, "Strategist")
    .SetChairman(Chairman.CreateStandard("qwen2.5:7b", ollama))
    .WithStandardDebate()
    .WithSystemPrompt("You are a software architecture expert.")
    .WithUserPrompt("Microservices vs Monolith for a 5-person startup?")
    .WithMaxRounds(4)
    .SaveResultTo("./deliberation.md")
    .Build()
    .ExecuteAsync();

Console.WriteLine(result.FinalVerdict);
```

> 📄 See [docs/QuickStart.md](docs/QuickStart.md) for a step-by-step walkthrough.

### Run

```bash
dotnet run
```

Delibera will run a structured, multi-round debate and write the full transcript and the
Chairman's verdict to `deliberation.md`.

---

## 💉 Dependency Injection

Register all Delibera services in one line:

```csharp
using Delibera.Core.DependencyInjection;

// Option A: With configuration binding (binds the "Delibera" section)
services.AddDelibera(configuration, "Delibera");

// Option B: With options delegate
services.AddDelibera(options =>
{
    options.Strategy = "Standard";
    options.MaxRounds = 4;
    options.Temperature = 0.7f;
    options.Compression.Enabled = true;
    options.Compression.Strategy = "Hybrid";
    options.Compression.TargetRatio = 0.5;
});

// Option C: Defaults only
services.AddDelibera();
```

Resolves these interfaces from DI:

| Interface             | Implementation       | Lifetime  |
| --------------------- | -------------------- | --------- |
| `ILLMProviderFactory` | `ProviderFactory`    | Singleton |
| `IRagProviderFactory` | `RagProviderFactory` | Singleton |
| `ICompressionFactory` | `CompressionService` | Singleton |
| `ICouncilBuilder`     | `CouncilBuilder`     | Transient |

### Configuration (`appsettings.json`)

```json
{
  "Delibera": {
    "Strategy": "Standard",
    "MaxRounds": 4,
    "Temperature": 0.7,
    "SystemPrompt": "You are a knowledgeable AI expert participating in a council debate.",
    "ResponseLanguage": "Russian",
    "MaxDegreeOfParallelism": 0,
    "Providers": {
      "DefaultType": "Ollama",
      "DefaultEndpoint": "http://localhost:11434",
      "ApiKey": "",
      "EmbeddingModel": "nomic-embed-text"
    },
    "Compression": {
      "Enabled": true,
      "Strategy": "Hybrid",
      "TargetRatio": 0.5,
      "EnableCache": true,
      "MaxCacheEntries": 256
    },
    "Rag": {
      "Enabled": false,
      "ProviderType": "Qdrant",
      "Host": "localhost",
      "Port": 6334,
      "CollectionName": "council_knowledge",
      "ConnectionString": null
    },
    "Output": {
      "Directory": "./debate_results",
      "SeparateFiles": true,
      "FilePrefix": null
    }
  }
}
```

To use **Ollama Cloud**, set `Providers:DefaultEndpoint` to `https://api.ollama.com` and put your
key in `Providers:ApiKey` (or `OllamaCloud:ApiKey` in the `DeliberaApp` section used by the
console app — see [ConsoleApp Examples](#-consoleapp-examples)).

---

## 🗜️ Context Compression

Automatically compress context between deliberation rounds — save **30–70% of tokens** without losing meaning.

| Strategy          | How It Works                                               | Best For                         |
| ----------------- | ---------------------------------------------------------- | -------------------------------- |
| **Semantic**      | Embeds sentences, ranks by relevance to topic, keeps top-N | Large knowledge contexts         |
| **Deduplication** | Removes semantically similar sentences across participants | Multi-model debates with overlap |
| **Summarization** | LLM produces a concise summary preserving key facts        | Maximum compression ratio        |
| **Hybrid**        | Dedup → Semantic → Summarize pipeline                      | Best overall quality             |
| **None**          | Pass-through (when disabled)                               | Debugging                        |

```csharp
using Delibera.Core.Compression;
using Delibera.Core.Providers.LLM;

var ollama = new OllamaProvider("http://localhost:11434");
var embeddings = new OllamaEmbeddingProvider(ollama, "nomic-embed-text");

var result = await new CouncilBuilder()
    .AddMember("llama3.2:3b", ollama, "Analyst")
    .AddMember("qwen2.5:7b", ollama, "Strategist")
    .SetChairman(Chairman.CreateStandard("qwen2.5:7b", ollama))
    .WithCompression(CompressionStrategy.Hybrid,
        llmProvider: ollama,
        modelName: "llama3.2:3b",
        embeddingProvider: embeddings)
    .WithCompressionOptions(new CompressionOptions { TargetRatio = 0.5 })
    .WithCompressionCache()
    .WithUserPrompt("Analyze our architecture options...")
    .WithMaxRounds(4)
    .Build()
    .ExecuteAsync();

Console.WriteLine(result.TokenStats?.ToSummary());
```

### Compression Pipeline

```
IContextCompressor
├── SemanticCompressor        ← Embedding-based sentence ranking
├── DeduplicationCompressor   ← Similarity-based duplicate removal
├── SummarizationCompressor   ← LLM-powered summarization
├── HybridCompressor          ← Multi-stage pipeline (Dedup → Semantic → Summarize)
└── PassThroughCompressor     ← No-op (when disabled)

CompressionFactory            ← Static factory (Create by enum or string)
CompressionService            ← DI-friendly wrapper around CompressionFactory
CompressionCache              ← SHA-256 keyed LRU cache
TokenCounter                  ← Heuristic token estimation
```

---

## 📚 RAG Integration

Use a dedicated **Qdrant** instance or your existing **PostgreSQL/pgvector** database as a vector store.

```csharp
using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Providers.RAG;

var ollama = new OllamaProvider("http://localhost:11434");
var embeddings = new OllamaEmbeddingProvider(ollama, "nomic-embed-text");

// pgvector — just add a connection string
var ragFactory = new RagProviderFactory();
var rag = ragFactory.CreatePgVector(
    embeddings,
    "Host=localhost;Database=council_vectors;Username=postgres;Password=postgres");

await rag.IndexDocumentAsync("my_collection", documentText);
var results = await rag.SearchAsync("my_collection", "query", limit: 5);

// Wire into a Knowledge Keeper
var kkMember = new CouncilMember("llama3.2:3b", ollama, "Knowledge Keeper");
var keeper = new KnowledgeKeeper(rag, kkMember, "my_knowledge");
await keeper.IndexFileAsync("./docs/architecture.md");
```

```
IRagProvider
├── QdrantRagProvider
│   └── QdrantVectorStore     ← Qdrant gRPC
└── PgVectorRagProvider
    └── PgVectorStore         ← PostgreSQL/pgvector

IEmbeddingProvider
└── OllamaEmbeddingProvider
```

The Knowledge Keeper is then attached to the council via `WithKnowledgeKeeper(...)` — see
the [RAG example](src/Delibera.ConsoleApp/Examples/RagExample.cs) for a full working demo.

---

## 🗣️ Debate Strategies

| Strategy              | Flow                                                    | Use Case                |
| --------------------- | ------------------------------------------------------- | ----------------------- |
| **StandardDebate**    | Initial → Critique → Improved → Verdict                 | General analysis        |
| **CritiqueDebate**    | Position → Attack → Defence → Judge                     | Hypothesis testing      |
| **ConsensusDebate**   | Perspectives → Common Ground → Consensus → Facilitator  | Optimal solution search |

Each strategy is implemented as an `IDebateStrategy` — see
[`src/Delibera.Core/Debate/`](src/Delibera.Core/Debate/) for the full source.

### Design Patterns

| Pattern             | Usage                                                                     |
| ------------------- | ------------------------------------------------------------------------- |
| **Factory**         | `ProviderFactory`, `RagProviderFactory`, `CompressionFactory`, `Chairman` |
| **Strategy**        | `IDebateStrategy`, `IContextCompressor`                                   |
| **Builder**         | `CouncilBuilder` fluent API                                               |
| **Template Method** | `DebateScenario` abstract base class                                      |
| **Cache**           | `CompressionCache` with SHA-256 keys                                      |
| **Observer**        | `OnRoundCompleted` event on `CouncilExecutor`                             |

---

## 📁 Output Files Structure

Each deliberation can be exported as a single file or as three separate Markdown documents:

```csharp
var result = await executor.ExecuteAsync();

// Save to 3 separate files
var (resultPath, statsPath, logsPath) = await result.SaveAllAsync("./output");
// Creates: debate_20260604_120000_result.md
//          debate_20260604_120000_statistics.md
//          debate_20260604_120000_logs.md

// Or save individually
await result.SaveToMarkdownAsync("result.md");
await result.SaveStatisticsAsync("statistics.md");
await result.SaveLogsAsync("logs.md");
```

| File              | Contents                                                                     |
| ----------------- | ---------------------------------------------------------------------------- |
| `*_result.md`     | Full deliberation transcript, rounds, and the Chairman's final verdict       |
| `*_statistics.md` | Token usage statistics with per-round breakdown                              |
| `*_logs.md`       | Execution logs (`ExecutionLog`) for Chairman, KK, compression & participants |

---

## 💻 ConsoleApp Examples

The repository ships with [`Delibera.ConsoleApp`](src/Delibera.ConsoleApp/), a runnable demo
project that exercises every feature. Run it from the repo root:

```bash
# Clone the repository
git clone https://github.com/delibera/Delibera.git
cd Delibera/src/Delibera.ConsoleApp

# Run a specific example
dotnet run -- --di                 # Dependency Injection
dotnet run -- --separate-files     # Save result.md, statistics.md, logs.md
dotnet run -- --compression        # Context compression demo
dotnet run -- --multiprovider      # Multi-provider (cloud + local) council
dotnet run -- --rag                # Qdrant-backed RAG with Knowledge Keeper
dotnet run -- --pgvector           # pgvector-backed RAG

# Or run the full default demo (reads appsettings.json)
dotnet run
```

The console app reads [`appsettings.json`](src/Delibera.ConsoleApp/appsettings.json) which uses
the `DeliberaApp` section — by default it points at Ollama Cloud and shows you how to wire
multiple providers, RAG, and compression in one place.

---

## 🛠️ Installation & Build

### Clone & Build

```bash
git clone https://github.com/delibera/Delibera.git
cd Delibera

# Build the entire solution
dotnet build --configuration Release

# Run the console demo
cd src/Delibera.ConsoleApp
dotnet run
```

### One-Command Infrastructure (Docker Compose)

Spin up **Qdrant + PostgreSQL/pgvector** in one shot. (Ollama is intentionally not included —
[install it natively](https://ollama.com/download) so the GPU driver is used directly.)

```bash
# From the repo root
docker compose up -d

# In another terminal, run the console app
cd src/Delibera.ConsoleApp
dotnet run
```

The compose stack exposes:

| Service       | URL                        | Purpose                       |
| ------------- | -------------------------- | ----------------------------- |
| Qdrant (REST) | `http://localhost:6333`    | Vector store UI / REST        |
| Qdrant (gRPC) | `localhost:6334`           | gRPC client (used by Delibera)|
| PostgreSQL    | `localhost:5432`           | pgvector RAG store            |

Default credentials: `postgres` / `postgres`, database `council_vectors`.

> If you already have these services running natively, just skip `docker compose` and
> point the console app at them — `appsettings.json` defaults to `localhost`.

### Manual Alternative (one container at a time)

```bash
# Run with Qdrant (Docker)
docker run -d -p 6333:6333 -p 6334:6334 qdrant/qdrant

# Run with pgvector (Docker)
docker run -d -p 5432:5432 -e POSTGRES_PASSWORD=postgres pgvector/pgvector:pg16

# Then enable the pgvector extension
docker exec -it <container> psql -U postgres -d council_vectors -c "CREATE EXTENSION IF NOT EXISTS vector;"
```

### NuGet Dependencies

| Package                  | Purpose                          |
| ------------------------ | -------------------------------- |
| `OllamaSharp`            | Ollama API client                |
| `Qdrant.Client`          | Qdrant vector DB gRPC client     |
| `Npgsql`                 | PostgreSQL ADO.NET provider      |
| `Pgvector`               | pgvector type support for Npgsql |
| `Microsoft.Extensions.*` | Configuration, DI and Options    |

---

## 📝 Logging (Microsoft.Extensions.Logging)

Delibera integrates with the standard .NET logging framework. Every debate event — Chairman
opening, Knowledge Keeper queries, compression operations, Operator interactions, participant
responses, errors — is forwarded to an injected `ILogger` (category `Delibera.Core.Council`)
**in addition to** the in-memory `ExecutionLog` collection and the `OnLog`/`OnError` events.

### Inject your logger via the builder

```csharp
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(b => { b.AddConsole(); b.SetMinimumLevel(LogLevel.Information); });

var result = await new CouncilBuilder()
   .AddMember("llama3.2:3b", ollama, "Analyst")
   .SetChairman(Chairman.CreateStandard("qwen2.5:7b", ollama))
   .WithStandardDebate()
   .WithUserPrompt("…")
   .WithLogger(loggerFactory.CreateLogger("Delibera"))
   .Build()
   .ExecuteAsync();
```

### Inject your logger factory via DI

```csharp
services.AddDelibera(configuration, loggerFactory, "Delibera");
// Any ICouncilBuilder resolved from the container is automatically decorated with a logger.
```

When no `ILogger` is configured, Delibera falls back to the legacy behaviour: events are only
captured in `DebateResult.ExecutionLogs` and the `OnLog`/`OnError` events.

---

## 🌐 Response Language Enforcement

Force **every** model response (participants, Chairman, Knowledge Keeper, Operator) to be in a
specific language, regardless of the language used in the prompt or retrieved RAG context.

```csharp
var result = await new CouncilBuilder()
   .AddMember("llama3.2:3b", ollama, "Analyst")
   .AddMember("qwen2.5:7b", ollama, "Strategist")
   .SetChairman(Chairman.CreateStandard("qwen2.5:7b", ollama))
   .WithStandardDebate()
   .WithUserPrompt("Microservices vs Monolith for a 5-person startup?")
   .WithResponseLanguage("Russian")   // ← force Russian answers
   .Build()
   .ExecuteAsync();
```

Or via configuration (`Delibera:ResponseLanguage`). Delibera injects a strict directive into every
system prompt: *“You MUST answer exclusively in {language}. Never use any other language…”*.
Leave empty/null to let the model pick a language from context (legacy behaviour).

---

## ⚡ Performance — Parallel Operator Requests

When participants delegate multiple tasks to the Operator within a round (via `[[OPERATOR: …]]`
markers), Delibera now executes them **in parallel** using `Parallel.ForEachAsync`, bounded by
`MaxDegreeOfParallelism`.

```csharp
var result = await new CouncilBuilder()
   .AddMember("llama3.2:3b", ollama, "Analyst")
   .WithOperator("llama3.2:3b", ollama, servers)
   .WithMaxDegreeOfParallelism(4)   // ← cap at 4 concurrent Operator tasks
   .Build()
   .ExecuteAsync();
```

Or via configuration (`Delibera:MaxDegreeOfParallelism`). Set `0` (default) for unbounded
parallelism — all delegated tasks in a round run concurrently.

---

## 🏛️ Architecture

```
Delibera.Core
├── Council/              ← CouncilBuilder, CouncilExecutor, Chairman, KnowledgeKeeper, Operator
├── Debate/               ← StandardDebate, CritiqueDebate, ConsensusDebate, DebateScenario
├── Compression/          ← Semantic / Deduplication / Summarization / Hybrid
├── Providers/
│   ├── LLM/              ← OllamaProvider, ChatClientLLMProvider, EmbeddingGeneratorProvider
│   ├── RAG/              ← QdrantRagProvider, PgVectorRagProvider
│   └── Mcp/              ← McpClientAdapter (Operator ↔ MCP servers)
├── Extensions/           ← MicrosoftAIExtensions (IChatClient ↔ ILLMProvider bridges)
├── DependencyInjection/  ← AddDelibera() / AddDeliberaChatClient() + CouncilOptions
├── Knowledge/            ← MarkdownKnowledgeBase
├── Models/               ← CouncilMember, DebateResult, DebateRound, TokenStatistics, DebateExecutionOptions, ...
└── Interfaces/           ← ILLMProvider, IRagProvider, IContextCompressor, IDebateStrategy, ...
```

---

## 🤝 Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on
development setup, coding standards, and the pull-request process.

---

## 📄 License

Delibera is released under the [MIT License](LICENSE).

Copyright © 2026 Delibera Project.

---

<div align="center">

**⚖️ Delibera — Thoughtful AI Decisions**

*Built with care for AI-powered collective intelligence*

</div>
