<div align="center">

# Delibera

### ⚖️ Thoughtful AI Decisions

**Collective decision making through structured AI deliberation — with RAG, pgvector, Knowledge Keeper, Operator (MCP tools), Chairman, Context Compression, Dependency Injection & Execution Logging.**

[![NuGet](https://img.shields.io/nuget/v/Delibera.Core.svg)](https://www.nuget.org/packages/Delibera.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-10B981.svg)](../LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-1F2937.svg)](https://dotnet.microsoft.com)

</div>

---

## What is Delibera?

**Delibera** is a C# / .NET 10 framework that orchestrates **multi-model deliberations** between LLMs.
Multiple models reason through a question across structured rounds, critique each other, and a
**Chairman** synthesises a balanced final verdict — optionally enriched by a **Knowledge Keeper**
backed by **Qdrant** or **PostgreSQL/pgvector** (RAG), with **context compression** to minimise tokens.

---

## ✨ Features

- 🏛️ **Multi-Model Councils** — orchestrate any number of LLM participants across structured debate rounds
- ⚖️ **Chairman Synthesis** — a dedicated moderator opens, regulates and synthesises the final verdict
- 📚 **Knowledge Keeper (RAG)** — per-round semantic retrieval with Qdrant or pgvector
- 🛠️ **Operator (MCP Tools)** — a micro-agent that delegates tasks to MCP servers (web search, file system, Notion, PostgreSQL…) on demand during the debate
- 🗜️ **Context Compression** — 4 strategies (Semantic, Deduplication, Summarization, Hybrid) save 30–70% of tokens
- 💉 **Dependency Injection** — `AddDelibera()` extension for `IServiceCollection`
- 📋 **Execution Logging** — `ExecutionLog` with `LogLevel` for Chairman, KK, Compression & participants
- 📁 **Separate File Output** — export `result.md`, `statistics.md`, `logs.md` independently
- 🤝 **Microsoft.Extensions.AI** — first-class `IChatClient` / `IEmbeddingGenerator` interop with logging & function-invocation middleware
- 🔌 **Interface-First** — clean abstractions for providers, factories, builders and executors
- 🧱 **Modern C# 12** — file-scoped namespaces, records, init-only properties, global usings

---

## 🚀 Quick Start

### 1. Install

```bash
dotnet add package Delibera.Core
```

### 2. Pull the recommended models

```bash
ollama pull llama3.2:3b       # council member
ollama pull qwen2.5:7b        # council member / chairman
ollama pull nomic-embed-text  # embeddings (RAG)
```

> ☁️ To use **Ollama Cloud** instead, just pass an API key — no local models required.
> See the [Configuration section](#-dependency-injection) below.

### 3. Run your first deliberation

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

---

## 💉 Dependency Injection

```csharp
using Delibera.Core.DependencyInjection;

// A: with configuration binding
services.AddDelibera(configuration, "Delibera");

// B: with options delegate
services.AddDelibera(options =>
{
    options.Strategy = "Standard";
    options.MaxRounds = 4;
    options.Compression.Enabled = true;
    options.Compression.Strategy = "Hybrid";
});

// C: defaults only
services.AddDelibera();
```

Resolved services:

| Interface             | Implementation       | Lifetime  |
| --------------------- | -------------------- | --------- |
| `ILLMProviderFactory` | `ProviderFactory`    | Singleton |
| `IRagProviderFactory` | `RagProviderFactory` | Singleton |
| `ICompressionFactory` | `CompressionService` | Singleton |
| `ICouncilBuilder`     | `CouncilBuilder`     | Transient |

---

## 🗜️ Context Compression

```csharp
using Delibera.Core.Compression;
using Delibera.Core.Providers.LLM;

var ollama = new OllamaProvider("http://localhost:11434");
var embeddings = new OllamaEmbeddingProvider(ollama, "nomic-embed-text");

var result = await new CouncilBuilder()
    .AddMember("llama3.2:3b", ollama)
    .SetChairman(Chairman.CreateStandard("qwen2.5:7b", ollama))
    .WithCompression(CompressionStrategy.Hybrid,
        llmProvider: ollama,
        modelName: "llama3.2:3b",
        embeddingProvider: embeddings)
    .WithCompressionCache()
    .WithUserPrompt("Analyze our architecture options...")
    .Build()
    .ExecuteAsync();

Console.WriteLine(result.TokenStats?.ToSummary());
```

Strategies: **Semantic**, **Deduplication**, **Summarization**, **Hybrid** (Dedup → Semantic → Summarize).

---

## 📚 RAG Integration

```csharp
using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Providers.RAG;

var ollama = new OllamaProvider("http://localhost:11434");
var embeddings = new OllamaEmbeddingProvider(ollama, "nomic-embed-text");

// pgvector — point at your existing PostgreSQL
var rag = new RagProviderFactory().CreatePgVector(
    embeddings,
    "Host=localhost;Database=council_vectors;Username=postgres;Password=postgres");

// …or Qdrant
// var rag = new RagProviderFactory().CreateQdrant(embeddings, "localhost", 6334);

var keeper = new KnowledgeKeeper(
    rag,
    new CouncilMember("llama3.2:3b", ollama, "Knowledge Keeper"),
    "my_knowledge");

await keeper.IndexFileAsync("./docs/architecture.md");
```

---

## 🛠️ Operator (MCP Tools)

The **Operator** is a lightweight micro-agent that connects the council to the outside world through
[**MCP (Model Context Protocol)**](https://modelcontextprotocol.io) servers. It exposes whatever tools
those servers provide — web search, file system access, Notion, PostgreSQL, a debate-history store, etc. —
to the debate participants.

**How it works**

1. The Operator connects to one or more MCP servers and discovers their tools on `InitializeAsync`.
2. Participants are told (in their system prompt) what the Operator can do, and can delegate a task at
   **any moment** during the debate by writing a marker in their message:

   ```
   [[OPERATOR: search the web for the latest EU AI Act timeline and save it to Notion]]
   ```

3. The Operator interprets the request with its **own (cheaper) LLM model**, picks and calls the right
   MCP tools, interprets the results, and returns a concise answer that is injected into the next round.
4. If the council uses **context compression**, the Operator can reuse the same strategy to compress
   large tool outputs before they re-enter the debate.

All Operator interactions are recorded per round and rendered in the final Markdown report under a
**🛠️ Operator Interactions** block.

### Quick usage

```csharp
using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;

var ollama = new OllamaProvider("http://localhost:11434");

// Define the MCP servers the Operator may use
var servers = new[]
{
    // stdio transport — launches a local MCP server process
    McpServerConfig.Stdio(
        name: "everything",
        command: "npx",
        arguments: new[] { "-y", "@modelcontextprotocol/server-everything" }),

    McpServerConfig.Stdio(
        name: "filesystem",
        command: "npx",
        arguments: new[] { "-y", "@modelcontextprotocol/server-filesystem", "/data" }),

    // …or an HTTP/SSE transport for a remote MCP server
    // McpServerConfig.Http(
    //     name: "remote",
    //     endpoint: "https://my-mcp-host.example.com/mcp",
    //     additionalHeaders: new Dictionary<string, string> { ["Authorization"] = "Bearer <token>" }),
};

var council = new CouncilBuilder()
    .AddMember("gpt-oss:20b",  ollama, "Optimist")
    .AddMember("llama3.1:8b",  ollama, "Skeptic")
    .WithChairman("gpt-oss:20b", ollama)
    // Operator uses its own cheaper model; reuseCompression shares the council's compressor
    .WithOperator("llama3.2:3b", ollama, servers, reuseCompression: true)
    .WithTopic("Should we migrate the data pipeline to Kafka?")
    .Build();

var result = await council.ExecuteAsync();
```

Prefer to build the `Operator` yourself? Pass a pre-built instance:

```csharp
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

### Dependency Injection

Configure the Operator declaratively in `appsettings.json` under `Delibera:Operator` (see the
configuration section below), then build the council from `CouncilOptions` as usual.

---

## 🗣️ Debate Strategies

| Strategy              | Flow                                                    | Use Case                |
| --------------------- | ------------------------------------------------------- | ----------------------- |
| **StandardDebate**    | Initial → Critique → Improved → Verdict                 | General analysis        |
| **CritiqueDebate**    | Position → Attack → Defence → Judge                     | Hypothesis testing      |
| **ConsensusDebate**   | Perspectives → Common Ground → Consensus → Facilitator  | Optimal solution search |

---

## 📁 Output Files

```csharp
var result = await executor.ExecuteAsync();

// Single combined file
await result.SaveToMarkdownAsync("./deliberation.md");

// Or three separate files
var (resultPath, statsPath, logsPath) = await result.SaveAllAsync("./output");
// → debate_<timestamp>_result.md      (transcript + verdict)
// → debate_<timestamp>_statistics.md  (per-round token usage)
// → debate_<timestamp>_logs.md        (ExecutionLog)
```

---

## 📦 Configuration (`appsettings.json`)

```json
{
  "Delibera": {
    "Strategy": "Standard",
    "MaxRounds": 4,
    "Temperature": 0.7,
    "Providers": {
      "DefaultType": "Ollama",
      "DefaultEndpoint": "http://localhost:11434",
      "ApiKey": "",
      "EmbeddingModel": "nomic-embed-text"
    },
    "Compression": { "Enabled": true, "Strategy": "Hybrid", "TargetRatio": 0.5 },
    "Rag":         { "Enabled": false, "ProviderType": "Qdrant", "Host": "localhost", "Port": 6334 },
    "Operator": {
      "Enabled": true,
      "ModelName": "llama3.2:3b",
      "ReuseCompression": true,
      "McpServers": [
        {
          "Name": "filesystem",
          "Transport": "Stdio",
          "Command": "npx",
          "Arguments": [ "-y", "@modelcontextprotocol/server-filesystem", "/data" ]
        },
        {
          "Name": "remote",
          "Transport": "Http",
          "Endpoint": "https://my-mcp-host.example.com/mcp",
          "AdditionalHeaders": { "Authorization": "Bearer <token>" }
        }
      ]
    },
    "Output":      { "Directory": "./debate_results", "SeparateFiles": true }
  }
}
```

For **Ollama Cloud**, set `Providers:DefaultEndpoint` to `https://api.ollama.com` and put your key
in `Providers:ApiKey`.

---

## 📦 NuGet Dependencies

| Package                  | Purpose                          |
| ------------------------ | -------------------------------- |
| `OllamaSharp`            | Ollama API client                |
| `Qdrant.Client`          | Qdrant vector DB gRPC client     |
| `Npgsql` / `Pgvector`    | PostgreSQL/pgvector support      |
| `ModelContextProtocol`   | MCP client for the Operator role |
| `Microsoft.Extensions.AI`| `IChatClient` / `IEmbeddingGenerator` abstractions & middleware |
| `Microsoft.Extensions.*` | Configuration, DI and Options    |

---

## 📚 Learn More

- 📖 Full README (architecture, design patterns, console app examples) → [github.com/delibera/Delibera](https://github.com/delibera/Delibera)
- 📄 Step-by-step walkthrough → [docs/QuickStart.md](https://github.com/delibera/Delibera/blob/main/docs/QuickStart.md)
- 🤝 [CONTRIBUTING.md](https://github.com/delibera/Delibera/blob/main/CONTRIBUTING.md)

---

## 📄 License

[MIT](../LICENSE) — Copyright © 2026 Delibera Project.

<div align="center">

**⚖️ Delibera — Thoughtful AI Decisions**

</div>
