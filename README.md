<div align="center">

<img src="img/delibera-horizontal-1920x480.png" alt="Delibera" width="640">

# Delibera

### ⚖️ Thoughtful AI Decisions

**Collective decision making through structured AI deliberation — with RAG, pgvector, Knowledge Keeper, 🛠️ Operator (MCP tools), Chairman, 🔥 Context Compression, 💉 Dependency Injection & 📋 Execution Logging**

[![NuGet](https://img.shields.io/nuget/v/Delibera.Core.svg)](https://www.nuget.org/packages/Delibera.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-10B981.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-1F2937.svg)](https://dotnet.microsoft.com)
[![C# 15](https://img.shields.io/badge/C%23-15.0--preview-239120.svg)](https://learn.microsoft.com/dotnet/csharp/)

🇷🇺 [Русская версия (README-RU.md)](README-RU.md)

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
| **🛠️ Operator (MCP Tools)**  | A micro-agent that delegates tasks to MCP servers (web, files, Marp, Notion, …) on demand during the debate |
| **🐘 Qdrant + pgvector**      | Pluggable vector stores — use a dedicated DB or your existing PostgreSQL              |
| **🗜️ Context Compression**   | 4 strategies (Semantic, Deduplication, Summarization, Hybrid) save 30–70% of tokens   |
| **💉 Dependency Injection**   | `AddDelibera()` extension for `IServiceCollection` with full options binding          |
| **📋 Execution Logging**      | `ExecutionLog` model with `LogLevel` — Chairman, KK, Compression & participant events |
| **📁 Separate File Output**   | Export `result.md`, `statistics.md`, and `logs.md` independently                      |
| **🔌 Interface-First**        | Clean abstractions for providers, factories, builders and executors                   |
| **🧱 Modern C# 15 (preview)** | Built on .NET 10 with `LangVersion=preview`, file-scoped namespaces, records, span/SIMD hot paths |

---

## 📑 Table of Contents

- [Quick Start](#-quick-start)
  - [Prerequisites & Models](#prerequisites--models)
  - [Installation](#installation)
  - [Minimal Example](#minimal-example)
  - [Run](#run)
- [Dependency Injection](#-dependency-injection)
- [Operator (MCP Tools)](#️-operator-mcp-tools)
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

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (≥ 10.0.301) — the project targets
  `net10.0` and builds with `LangVersion=preview` to enable **C# 15** features. See
  [docs/NET10-Upgrade.md](docs/NET10-Upgrade.md) for the full migration notes.
- A running [Ollama](https://ollama.com) instance — local (`ollama serve`) or
  [Ollama Cloud](https://ollama.com/cloud) (just an API key, no install).
- The minimal set of models listed below.
- **Optional — for the Operator role:** [Node.js + npx](https://nodejs.org) to launch MCP servers
  (e.g. `@playwright/mcp`, `@marp-team/marp-cli`). See [Operator (MCP Tools)](#️-operator-mcp-tools).

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

## 🛠️ Operator (MCP Tools)

The **Operator** is a lightweight micro-agent that connects the council to the outside world through
[**MCP (Model Context Protocol)**](https://modelcontextprotocol.io) servers. It exposes whatever tools
those servers provide — web browsing, file system access, Marp presentation generation, Notion,
PostgreSQL, etc. — to the debate participants.

**How it works**

1. The Operator connects to one or more MCP servers and discovers their tools on `InitializeAsync`.
2. Participants are told (in their system prompt) what the Operator can do, and can delegate a task at
   **any moment** during the debate by writing a marker in their message:

   ```
   [[OPERATOR: open https://modelcontextprotocol.io and summarize what MCP is]]
   ```

3. The Operator interprets the request with its **own (cheaper) LLM model**, picks and calls the right
   MCP tools, interprets the results, and returns a concise answer that is injected into the next round.
4. If the council uses **context compression**, the Operator can reuse the same strategy to compress
   large tool outputs before they re-enter the debate. Its `DisposeAsync` is a `ValueTask` (.NET 10
   `IAsyncDisposable`) so MCP clients are released without a `Task` allocation.

All Operator interactions are recorded per round and rendered in the final Markdown report under a
**🛠️ Operator Interactions** block.

### Configuring MCP servers

```csharp
using Delibera.Core.Models;

var servers = new[]
{
    // stdio transport — launches a local MCP server process
    McpServerConfig.Stdio(
        name: "browser",
        command: "npx",
        arguments: new[] { "-y", "@playwright/mcp@latest", "--headless" }),

    McpServerConfig.Stdio(
        name: "marp",
        command: "npx",
        arguments: new[] { "-y", "@marp-team/marp-cli", "--server", "./out" }),

    // …or an HTTP/SSE transport for a remote MCP server
    // McpServerConfig.Http(
    //     name: "remote",
    //     endpoint: "https://my-mcp-host.example.com/mcp",
    //     additionalHeaders: new Dictionary<string, string> { ["Authorization"] = "Bearer <token>" }),
};
```

| Server       | Transport | Launch command                                  | What it provides                                     |
| ------------ | --------- | ----------------------------------------------- | ---------------------------------------------------- |
| 🌐 `browser` | stdio     | `npx -y @playwright/mcp@latest --headless`      | Site navigation, page reading, clicks, screenshots   |
| 🎯 `marp`    | stdio     | `npx -y @marp-team/marp-cli --server <dir>`     | Generating presentations (HTML/PDF/PPTX) from Markdown |

### Quick usage (inside a council)

```csharp
using Delibera.Core.Council;
using Delibera.Core.Providers.LLM;

var ollama = new OllamaProvider("http://localhost:11434");

var council = new CouncilBuilder()
    .AddMember("llama3.2:3b", ollama, "Optimist")
    .AddMember("qwen2.5:7b",  ollama, "Skeptic")
    .SetChairman(Chairman.CreateStandard("qwen2.5:7b", ollama))
    // Operator uses its own cheaper model; reuseCompression shares the council's compressor
    .WithOperator("llama3.2:3b", ollama, servers, reuseCompression: true)
    .WithStandardDebate()
    .WithUserPrompt("Research the latest .NET 10 features and prepare a short summary.")
    .WithMaxRounds(4)
    .Build()
    .ExecuteAsync();
```

Prefer to build the `Operator` yourself? Pass a pre-built instance:

```csharp
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers.Mcp;

var @operator = new Operator(
    new CouncilMember("llama3.2:3b", ollama, "Operator"),
    new IMcpClient[] { new McpClientAdapter(servers[0]), new McpClientAdapter(servers[1]) },
    compressor: null,            // optional IContextCompressor
    compressionOptions: null);   // optional CompressionOptions

var council = new CouncilBuilder()
    /* …members… */
    .WithOperator(@operator)
    .Build();
```

### Direct usage (without a council)

```csharp
await using var @operator = new Operator(
    new CouncilMember("llama3.2:3b", ollama, "Operator"),
    new IMcpClient[] { new McpClientAdapter(servers[0]), new McpClientAdapter(servers[1]) });

await @operator.InitializeAsync();

var result = await @operator.ExecuteTaskAsync(
    "Open https://modelcontextprotocol.io and briefly summarize what MCP is.");

Console.WriteLine(result.FinalAnswer);
```

### Dependency Injection

Configure the Operator declaratively in `appsettings.json` under `Delibera:Operator`:

```json
{
  "Delibera": {
    "Operator": {
      "Enabled": true,
      "ModelName": "llama3.2:3b",
      "ReuseCompression": true,
      "McpServers": [
        {
          "Name": "browser",
          "Transport": "Stdio",
          "Command": "npx",
          "Arguments": [ "-y", "@playwright/mcp@latest", "--headless" ]
        },
        {
          "Name": "remote",
          "Transport": "Http",
          "Endpoint": "https://my-mcp-host.example.com/mcp",
          "AdditionalHeaders": { "Authorization": "Bearer <token>" }
        }
      ]
    }
  }
}
```

> ▶️ A complete, runnable demo lives in
> [`OperatorMcpToolsExample.cs`](src/Delibera.ConsoleApp/Examples/OperatorMcpToolsExample.cs).
> Run it with `dotnet run --project src/Delibera.ConsoleApp -- --operator-mcp`.
> For the full technical write-up, see [docs/NET10-Upgrade.md](docs/NET10-Upgrade.md).

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
dotnet run -- --operator           # Operator role basics (MCP tools)
dotnet run -- --operator-mcp       # 🆕 Operator with browser + Marp MCP servers

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
| `ModelContextProtocol`   | MCP client for the Operator role |
| `Microsoft.Extensions.*` | Configuration, DI and Options    |

---

## 🏛️ Architecture

```
Delibera.Core
├── Council/              ← CouncilBuilder, CouncilExecutor, Chairman, KnowledgeKeeper, Operator
├── Debate/               ← StandardDebate, CritiqueDebate, ConsensusDebate
├── Compression/          ← Semantic / Deduplication / Summarization / Hybrid
├── Providers/
│   ├── LLM/              ← OllamaProvider, OllamaEmbeddingProvider
│   ├── RAG/              ← QdrantRagProvider, PgVectorRagProvider
│   └── Mcp/              ← McpClientAdapter (Operator ↔ MCP servers)
├── DependencyInjection/  ← AddDelibera() + CouncilOptions
├── Knowledge/            ← MarkdownKnowledgeBase
├── Models/               ← CouncilMember, DebateResult, DebateRound, TokenStatistics, ...
└── Interfaces/           ← ILLMProvider, IRagProvider, IContextCompressor, IOperator, IMcpClient, ...
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
