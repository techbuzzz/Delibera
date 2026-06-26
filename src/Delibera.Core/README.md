# [Delibera](https://www.nuget.org/packages/Delibera.Core)

### ⚖️ Thoughtful AI Decisions

**Collective decision making through structured AI deliberation — with RAG, pgvector, Knowledge Keeper, Operator (MCP tools), Chairman, Context Compression, Dependency Injection & Execution Logging.**

[![NuGet](https://img.shields.io/nuget/v/Delibera.Core.svg)](https://www.nuget.org/packages/Delibera.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-10B981.svg)](https://github.com/techbuzzz/Delibera/blob/develop/LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-1F2937.svg)](https://dotnet.microsoft.com)
[![C# 15](https://img.shields.io/badge/C%23-15.0--preview-239120.svg)](https://learn.microsoft.com/dotnet/csharp/)

---

## What is Delibera?

**Delibera** is a C# / .NET 10 framework that orchestrates **multi-model deliberations** between LLMs.
Multiple AI models reason through a question across structured rounds, critique each other's answers,
and a **Chairman** weighs the arguments to synthesise a balanced final verdict — enriched by a
**Knowledge Keeper** backed by **Qdrant** or **PostgreSQL/pgvector** (RAG), with **intelligent context
compression** to minimise token usage.

The name comes from *deliberation* — the careful weighing of evidence and viewpoints before reaching
a decision. Delibera brings that discipline to AI, helping teams reach **thoughtful, well-reasoned
outcomes** rather than single-model guesses.

---

## ✨ Features

- 🏛️ **Multi-Model Councils** — orchestrate any number of LLM participants across structured debate rounds
- ⚖️ **Chairman Synthesis** — a dedicated moderator opens, regulates and synthesises the final verdict
- 📚 **Knowledge Keeper (RAG)** — per-round semantic retrieval with Qdrant or pgvector
- 🐘 **Qdrant + pgvector** — pluggable vector stores (dedicated DB or your existing PostgreSQL)
- 🛠️ **Operator (MCP Tools)** — a micro-agent that delegates tasks to MCP servers (web search, file system, Notion, PostgreSQL…) on demand during the debate
- 🗜️ **Context Compression** — 4 strategies (Semantic, Deduplication, Summarization, Hybrid) save 30–70% of tokens
- 💉 **Dependency Injection** — `AddDelibera()` extension for `IServiceCollection` with full options binding
- 📋 **Execution Logging** — `ExecutionLog` model with `ExecutionLogLevel` for Chairman, KK, Compression & participants
- 📝 **Microsoft.Extensions.Logging** — inject your own `ILogger`/`ILoggerFactory`; every debate event is forwarded to the host's logging pipeline (in addition to the in-memory `ExecutionLog` collection)
- 🛡️ **Polly v8 Resilience** — `IHttpClientFactory` + named `Microsoft.Extensions.Http.Resilience` retry pipelines (`Delibera.Local`, `Delibera.Cloud`, `Delibera.Default`) for transient HTTP failures (connection drops, 408/429/5xx, Cloudflare 524). Configure via `CouncilOptions.Resilience`. Custom pipelines registerable through `AddDeliberaResiliencePipeline(name, build)`.
- 🌐 **Response Language Enforcement** — force every model (participants, Chairman, Knowledge Keeper, Operator) to answer in a specific language, regardless of the prompt or retrieved context
- ⚡ **Parallel Operator Requests** — `[[OPERATOR: …]]` tasks delegated within a round run in parallel, bounded by `MaxDegreeOfParallelism`
- 📁 **Separate File Output** — export `result.md`, `statistics.md`, `logs.md` independently
- 🤝 **Microsoft.Extensions.AI** — first-class `IChatClient` / `IEmbeddingGenerator` interop with logging & function-invocation middleware
- 🔌 **Interface-First** — clean abstractions for providers, factories, builders and executors
- 🧱 **Modern C# 15** — file-scoped namespaces, records, init-only properties, global usings

---

## 🚀 Quick Start

### 1. Install

```bash
dotnet add package Delibera.Core
```

### 2. Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A running [Ollama](https://ollama.com) instance — local (`ollama serve`) or [Ollama Cloud](https://ollama.com/cloud) (just an API key, no install).

### 3. Pull the recommended models

```bash
ollama pull llama3.2:3b       # council member
ollama pull qwen2.5:7b        # council member / chairman
ollama pull nomic-embed-text  # embeddings (RAG)
```

> ☁️ To use **Ollama Cloud** instead, just pass an API key — no local models required.
> See the [Configuration section](#-configuration-appsettingsjson) below.

### 4. Run your first deliberation

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

Register all Delibera services in one line:

```csharp
using Delibera.Core.DependencyInjection;

// A: with configuration binding (binds the "Delibera" section)
services.AddDelibera(configuration, "Delibera");

// B: with options delegate
services.AddDelibera(options =>
{
	 options.Strategy = "Standard";
	 options.MaxRounds = 4;
	 options.Temperature = 0.7f;
	 options.Compression.Enabled = true;
	 options.Compression.Strategy = "Hybrid";
	 options.Compression.TargetRatio = 0.5;
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

## 🤝 Microsoft.Extensions.AI Interop

Delibera integrates with [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
through `ChatClientLLMProvider` and `EmbeddingGeneratorProvider`. Adapt **any** `IChatClient` /
`IEmbeddingGenerator` (OpenAI, Azure OpenAI, Ollama, custom…) and use the full middleware pipeline
(logging, function invocation, telemetry).

```csharp
using Delibera.Core.Providers.LLM;
using Microsoft.Extensions.AI;
using OpenAI;

var openAi = new OpenAIClient(apiKey);

// IChatClient — works with any MEAI-compatible client
var chatClient = openAi.GetChatClient("gpt-4o").AsIChatClient();
var llm = new ChatClientLLMProvider(chatClient);

// IEmbeddingGenerator — for Knowledge Keeper / compression
var embeddingGen = openAi.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator();
var embeddings = new EmbeddingGeneratorProvider(embeddingGen);

var result = await new CouncilBuilder()
	 .AddMember("gpt-4o", llm, "Analyst")
	 .SetChairman(Chairman.CreateStandard("gpt-4o", llm))
	 .WithStandardDebate()
	 .WithUserPrompt("Should we adopt event sourcing?")
	 .Build()
	 .ExecuteAsync();
```

Streaming via `ChatStreamAsync` and DI helpers (`AddDeliberaChatClient`) are also included. Fully
backward compatible with the existing `OllamaProvider` / `OllamaEmbeddingProvider` APIs.

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
	 .AddMember("llama3.2:3b", ollama)
	 .SetChairman(Chairman.CreateStandard("qwen2.5:7b", ollama))
	 .WithCompression(CompressionStrategy.Hybrid,
		  llmProvider: ollama,
		  modelName: "llama3.2:3b",
		  embeddingProvider: embeddings)
	 .WithCompressionOptions(new CompressionOptions { TargetRatio = 0.5 })
	 .WithCompressionCache()
	 .WithUserPrompt("Analyze our architecture options...")
	 .Build()
	 .ExecuteAsync();

Console.WriteLine(result.TokenStats?.ToSummary());
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

…or use **Qdrant**:

```csharp
var rag = ragFactory.CreateQdrant(embeddings, "localhost", 6334);
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
	 McpServerConfig.Stdio(
		  name: "everything",
		  command: "npx",
		  arguments: new[] { "-y", "@modelcontextprotocol/server-everything" }),

	 McpServerConfig.Stdio(
		  name: "filesystem",
		  command: "npx",
		  arguments: new[] { "-y", "@modelcontextprotocol/server-filesystem", "/data" }),

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

---

## 🗣️ Debate Strategies

| Strategy              | Flow                                                    | Use Case                |
| --------------------- | ------------------------------------------------------- | ----------------------- |
| **StandardDebate**    | Initial → Critique → Improved → Verdict                 | General analysis        |
| **CritiqueDebate**    | Position → Attack → Defence → Judge                     | Hypothesis testing      |
| **ConsensusDebate**   | Perspectives → Common Ground → Consensus → Facilitator  | Optimal solution search |

Each strategy is implemented as an `IDebateStrategy` — combine with `Builder`, `Template Method`
(`DebateScenario`) and `Factory` patterns (`ProviderFactory`, `RagProviderFactory`, `CompressionFactory`,
`Chairman`) to compose custom flows.

---

## 📝 Logging (Microsoft.Extensions.Logging)

Delibera integrates with the standard .NET logging framework. Every debate event — Chairman
opening, Knowledge Keeper queries, compression operations, Operator interactions, participant
responses, errors — is forwarded to an injected `ILogger` (category `Delibera.Core.Council`)
**in addition to** being recorded in the in-memory `ExecutionLog` collection and the `OnLog` event.

### Inject your logger via the builder

```csharp
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
{
   builder.AddConsole();
   builder.SetMinimumLevel(LogLevel.Information);
});

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
captured in the `DebateResult.ExecutionLogs` collection and the `OnLog`/`OnError` events.

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

Or via configuration:

```json
{
  "Delibera": {
    "ResponseLanguage": "Russian"
  }
}
```

Delibera injects a strict directive into every system prompt:
> *IMPORTANT: You MUST answer exclusively in {language}. Never use any other language, regardless
> of the language used in the question, retrieved context, or other participants' messages.*

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

Or via configuration:

```json
{
  "Delibera": {
    "MaxDegreeOfParallelism": 4
  }
}
```

Set `0` (default) for unbounded parallelism — all delegated tasks in a round run concurrently.

---

## 📁 Output Files

Each deliberation can be exported as a single file or as three separate Markdown documents:

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

| File              | Contents                                                                     |
| ----------------- | ---------------------------------------------------------------------------- |
| `*_result.md`     | Full deliberation transcript, rounds, and the Chairman's final verdict       |
| `*_statistics.md` | Token usage statistics with per-round breakdown                              |
| `*_logs.md`       | Execution logs (`ExecutionLog`) for Chairman, KK, compression & participants |

---

## 📦 Configuration (`appsettings.json`)

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
		"CollectionName": "council_knowledge"
	 },
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
	 "Output": {
		"Directory": "./debate_results",
		"SeparateFiles": true
	 }
  }
}
```

For **Ollama Cloud**, set `Providers:DefaultEndpoint` to `https://api.ollama.com` and put your key
in `Providers:ApiKey`.

---

## 📦 NuGet Dependencies

| Package                   | Purpose                                                     |
| ------------------------- | ----------------------------------------------------------- |
| `Microsoft.Extensions.AI` | `IChatClient` / `IEmbeddingGenerator` abstractions & middleware |
| `OllamaSharp`             | Ollama API client                                           |
| `Qdrant.Client`           | Qdrant vector DB gRPC client                                |
| `Npgsql` / `Pgvector`     | PostgreSQL/pgvector support                                 |
| `ModelContextProtocol`    | MCP client for the Operator role                            |
| `Microsoft.Extensions.*`  | Configuration, DI and Options                               |

---

## 📚 Learn More

- 📖 Full README (architecture, design patterns, console app examples) → [github.com/techbuzzz/Delibera](https://github.com/techbuzzz/Delibera)
- 📄 Step-by-step walkthrough → [docs/QuickStart.md](https://github.com/techbuzzz/Delibera/blob/develop/docs/QuickStart.md)
- 💻 Console app examples → [src/Delibera.ConsoleApp](https://github.com/techbuzzz/Delibera/tree/develop/src/Delibera.ConsoleApp)
- 🤝 [CONTRIBUTING.md](https://github.com/techbuzzz/Delibera/blob/develop/CONTRIBUTING.md)

---

## 📄 License

[MIT](https://github.com/techbuzzz/Delibera/blob/develop/LICENSE) — Copyright © 2026 Delibera Project.

**⚖️ Delibera — Thoughtful AI Decisions**

*Built with care for AI-powered collective intelligence*