<div align="center">

# Delibera

### ⚖️ Thoughtful AI Decisions

**Collective decision making through structured AI deliberation — with RAG, pgvector, Knowledge Keeper, Chairman, Context Compression, Dependency Injection & Execution Logging.**

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
- 🗜️ **Context Compression** — 4 strategies (Semantic, Deduplication, Summarization, Hybrid) save 30–70% of tokens
- 💉 **Dependency Injection** — `AddDelibera()` extension for `IServiceCollection`
- 📋 **Execution Logging** — `ExecutionLog` with `LogLevel` for Chairman, KK, Compression & participants
- 📁 **Separate File Output** — export `result.md`, `statistics.md`, `logs.md` independently
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
