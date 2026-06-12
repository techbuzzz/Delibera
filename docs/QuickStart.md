<div align="center">

<img src="../img/delibera-horizontal-1920x480.png" alt="Delibera" width="480">

# Delibera — Quick Start

### ⚖️ Thoughtful AI Decisions

</div>

This guide walks you through your first AI deliberation with **Delibera** in a few minutes.
By the end you'll have a multi-model debate running locally and a Markdown transcript on disk.

---

## 1. Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A running [Ollama](https://ollama.com) instance — either:
  - **Local:** [install Ollama](https://ollama.com/download) and run `ollama serve`, or
  - **Cloud:** sign up for [Ollama Cloud](https://ollama.com/cloud) and grab an API key
    (no install, no GPU required).

### Models to install

Pick **one** of the model sets below based on your hardware. The 🟡 Standard set is recommended
for most use cases.

#### 🟢 Minimal — low-end hardware / smoke tests (≈ 2 GB total)

```bash
ollama pull llama3.2:1b
ollama pull qwen2.5:1.5b
ollama pull nomic-embed-text
```

#### 🟡 Standard — recommended (≈ 7 GB total)

```bash
ollama pull llama3.2:3b
ollama pull qwen2.5:7b
ollama pull nomic-embed-text
```

#### 🔴 High-Performance — GPU with ≥ 24 GB VRAM or Ollama Cloud (≈ 30+ GB)

```bash
ollama pull llama3.1:8b
ollama pull qwen2.5:14b
ollama pull mistral:7b
ollama pull nomic-embed-text
```

> ℹ️ Ollama Cloud uses the same model names — no local download is required, just an API key.

---

## 2. Install the Package

```bash
dotnet add package Delibera.Core
```

Or clone the repository and reference `Delibera.Core` directly:

```bash
git clone https://github.com/delibera/Delibera.git
cd Delibera
```

---

## 3. Your First Deliberation

Create a console project and paste the following:

```csharp
// Program.cs
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

Run it:

```bash
dotnet run
```

Delibera will run a structured, multi-round debate and write the full transcript and the
Chairman's verdict to `deliberation.md`.

> ☁️ **Using Ollama Cloud?** Replace the endpoint with `https://api.ollama.com` and pass the API key:
> `factory.CreateOllama("https://api.ollama.com", "YOUR_API_KEY")`.

---

## 4. Using Dependency Injection

```csharp
using Delibera.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Binds the "Delibera" section of appsettings.json
services.AddDelibera(configuration, "Delibera");

var provider = services.BuildServiceProvider();
var builder = provider.GetRequiredService<ICouncilBuilder>();
```

`appsettings.json`:

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

> For **Ollama Cloud**, set `Providers:DefaultEndpoint` to `https://api.ollama.com` and
> `Providers:ApiKey` to your key.

---

## 5. Adding Context Compression

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
    .WithCompressionCache()
    .WithUserPrompt("Analyze our architecture options...")
    .WithMaxRounds(4)
    .Build()
    .ExecuteAsync();

Console.WriteLine(result.TokenStats?.ToSummary());
```

Compression saves roughly **30–70% of tokens** without losing the meaning of the debate context.

---

## 6. Adding RAG with Qdrant

```bash
docker run -d -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Providers.RAG;

var ollama = new OllamaProvider("http://localhost:11434");
var embeddings = new OllamaEmbeddingProvider(ollama, "nomic-embed-text");

var ragFactory = new RagProviderFactory();
var rag = ragFactory.CreateQdrant(embeddings, "localhost", 6334);

var kkMember = new CouncilMember("llama3.2:3b", ollama, "Knowledge Keeper");
var keeper = new KnowledgeKeeper(rag, kkMember, "architecture_kb");
await keeper.IndexFileAsync("./docs/architecture.md");

var result = await new CouncilBuilder()
    .AddMember("llama3.2:3b", ollama, "Backend Expert")
    .AddMember("qwen2.5:7b", ollama, "DevOps Expert")
    .SetChairman(Chairman.CreateStandard("qwen2.5:7b", ollama))
    .WithKnowledgeKeeper(keeper)
    .WithStandardDebate()
    .WithUserPrompt("Our startup has 4 developers. Microservices or monolith?")
    .WithMaxRounds(4)
    .Build()
    .ExecuteAsync();
```

> 💡 Swap `CreateQdrant(...)` for `CreatePgVector(embeddings, connectionString)` to use
> PostgreSQL/pgvector instead.

---

## 7. Saving Separate Output Files

```csharp
var (resultPath, statsPath, logsPath) = await result.SaveAllAsync("./output");
// → debate_<timestamp>_result.md
// → debate_<timestamp>_statistics.md
// → debate_<timestamp>_logs.md
```

Each file is plain Markdown — ideal for committing to git, posting to chat tools, or feeding
back into another LLM.

---

## 8. Explore the ConsoleApp

The repository ships with a runnable demo under
[`src/Delibera.ConsoleApp/`](../src/Delibera.ConsoleApp/) that exercises every feature:

| Example              | Command                          | What it shows                                  |
| -------------------- | -------------------------------- | ---------------------------------------------- |
| Quick start          | `dotnet run`                     | Default debate from `appsettings.json`         |
| Dependency injection| `dotnet run -- --di`             | `AddDelibera()` + `ICouncilBuilder` resolution |
| Separate file output | `dotnet run -- --separate-files` | `SaveAllAsync()` — three Markdown files        |
| Context compression  | `dotnet run -- --compression`    | All 4 strategies + cache + token stats         |
| Multi-provider       | `dotnet run -- --multiprovider`  | Cloud + local models in one council            |
| RAG (Qdrant)         | `dotnet run -- --rag`            | Document indexing + Knowledge Keeper           |
| RAG (pgvector)       | `dotnet run -- --pgvector`       | PostgreSQL-backed vector search                |

---

## Next Steps

- Read the [README](../README.md) for the full feature overview, architecture, and design patterns.
- See [CONTRIBUTING.md](../CONTRIBUTING.md) if you'd like to contribute.

---

<div align="center">

**⚖️ Delibera — Thoughtful AI Decisions**

</div>
