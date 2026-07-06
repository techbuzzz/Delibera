# ChatClientLLMProvider — Pluggable LLM Backends via Microsoft.Extensions.AI

`ChatClientLLMProvider` is Delibera's universal LLM provider. It wraps **any**
`Microsoft.Extensions.AI.IChatClient` implementation and exposes it as a standard
`Delibera.Core.Providers.LLM.ILLMProvider`. Because every modern .NET LLM SDK ships an
`IChatClient`, you can plug **OpenAI, Azure OpenAI, Anthropic, Ollama, GitHub Copilot,
Microsoft Foundry, LM Studio, vLLM, LocalAI, ONNX**, and more into a Delibera council
without writing a bespoke provider for each one.

The companion `Delibera.Core.Extensions.MicrosoftAIExtensions` class bridges the two
abstraction worlds in both directions:

| From → To                                       | Helper                                                            |
| ------------------------------------------------- | ----------------------------------------------------------------- |
| `IChatClient` → `ILLMProvider`                   | `.AsLLMProvider()` or `new ChatClientLLMProvider(...)`            |
| `IEmbeddingGenerator<string, Embedding<float>>` → `IEmbeddingProvider` | `.AsEmbeddingProvider()`               |
| `ILLMProvider` → `IChatClient`                   | `.AsChatClient()` (returns the inner client when already wrapped) |
| Compose middleware (function invocation, logging) | `.WithMiddleware(enableFunctionInvocation, loggerFactory)`       |

---

## 📦 Supported Providers

Any NuGet package that exposes `Microsoft.Extensions.AI.IChatClient` works out of the box.
Add the package to your project, obtain the `IChatClient`, and hand it to
`ChatClientLLMProvider`.

| Provider                         | NuGet Package                                          | Auth / Endpoint                                       | `IChatClient` source                                              |
| -------------------------------- | ------------------------------------------------------ | ----------------------------------------------------- | ---------------------------------------------------------------- |
| **OpenAI**                       | `Microsoft.Extensions.AI.OpenAI`                       | API key (`OPENAI_API_KEY`)                            | `new OpenAI.Chat.ChatClient("gpt-4o", key).AsIChatClient()`      |
| **Azure OpenAI**                 | `Azure.AI.OpenAI` + `Microsoft.Extensions.AI.OpenAI`   | Azure endpoint + key / Entra ID                       | `new AzureOpenAIClient(...).GetChatClient(deployment).AsIChatClient()` |
| **Microsoft Foundry**            | `Microsoft.Extensions.AI.AzureAIInference`             | Foundry endpoint + key                                | `new AzureAIInferenceClient(...).AsIChatClient()`                |
| **Anthropic Claude**             | `Microsoft.Extensions.AI.Anthropic` *(community/port)* | API key (`ANTHROPIC_API_KEY`)                         | provider-specific builder                                         |
| **Ollama (local or Cloud)**      | `OllamaSharp` *(already a Delibera dependency)*         | Local `http://localhost:11434` or Cloud `https://api.ollama.com` + key | `new OllamaApiClient(uri, model)` or `OllamaProvider.AsChatClient()` |
| **GitHub Copilot**               | `Microsoft.Extensions.AI.GitHubCopilot`                | GitHub token                                           | `GitHubCopilotChatClient(...)`                                   |
| **LM Studio** (OpenAI-compatible)| `Microsoft.Extensions.AI.OpenAI`                       | `http://localhost:1234/v1` + any string key           | `new OpenAI.Chat.ChatClient(...).AsIChatClient()` with custom base URL |
| **vLLM / LocalAI** (OpenAI-compat)| `Microsoft.Extensions.AI.OpenAI`                       | server URL + any string key                           | same as LM Studio                                                |
| **ONNX Runtime GenAI**           | `Microsoft.ML.GenAI` / `Microsoft.Extensions.AI.ONNX` | local model path                                       | `OnnxGenAIChatClient(...)`                                       |
| **Custom `IChatClient`**         | —                                                      | your own implementation                                | implement `IChatClient` or derive from `DelegatingChatClient`    |

> ℹ️ Delibera's `OllamaProvider` already natively implements the `IChatClient` contract via
> OllamaSharp — no extra NuGet package is required for the Ollama path. The same is true for
> the embedding side via `OllamaProvider.AsEmbeddingGenerator()`.

---

## 🚀 Minimal Example — OpenAI

```csharp
using Microsoft.Extensions.AI;
using OpenAI;
using Delibera.Core.Providers.LLM;

// 1. Build an IChatClient from the OpenAI SDK
var openAi = new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
IChatClient chatClient = openAi.GetChatClient("gpt-4o-mini").AsIChatClient();

// 2. Wrap it as a Delibera ILLMProvider
var llmProvider = new ChatClientLLMProvider(chatClient, "OpenAI");

// 3. Use it like any other Delibera provider
string response = await llmProvider.ChatAsync(
    model: "gpt-4o-mini",
    systemPrompt: "You are a helpful assistant.",
    userPrompt: "What is the capital of France?",
    temperature: 0.7f);

Console.WriteLine(response); // The capital of France is Paris.
```

### Equivalent one-liner using the extension method

```csharp
ILLMProvider llmProvider = chatClient.AsLLMProvider("OpenAI");
```

---

## 🏛️ OpenAI in a Delibera Council

```csharp
using Microsoft.Extensions.AI;
using OpenAI;
using Delibera.Core.Council;
using Delibera.Core.Extensions;
using Delibera.Core.Providers;
using Delibera.Core.Providers.LLM;

var openAi = new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
IChatClient chatClient = openAi.GetChatClient("gpt-4o-mini").AsIChatClient();

// Optional: add Microsoft.Extensions.AI middleware (function invocation + logging)
IChatClient decorated = chatClient.WithMiddleware(enableFunctionInvocation: true);

// Adopt as a Delibera provider
ILLMProvider openAiProvider = decorated.AsLLMProvider("OpenAI");

var result = await new CouncilBuilder()
    .AddMember("gpt-4o-mini", openAiProvider, "Analyst")
    .AddMember("gpt-4o",      openAiProvider, "Skeptic")
    .SetChairman(Chairman.CreateStandard("gpt-4o-mini", openAiProvider))
    .WithStandardDebate()
    .WithSystemPrompt("You are a software architecture expert.")
    .WithUserPrompt("Microservices vs Monolith for a 5-person startup?")
    .WithMaxRounds(4)
    .SaveResultTo("./openai_debate.md")
    .Build()
    .ExecuteAsync();

Console.WriteLine(result.FinalVerdict);
```

---

## ☁️ Azure OpenAI

```csharp
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

var azure = new AzureOpenAIClient(
    new Uri("https://<your-resource>.openai.azure.com"),
    new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!));

IChatClient chatClient = azure.GetChatClient("gpt-4o-deployment").AsIChatClient();

var llmProvider = new ChatClientLLMProvider(chatClient, "AzureOpenAI");
string answer = await llmProvider.ChatAsync(
    "gpt-4o-deployment",
    "You are a concise assistant.",
    "Summarise the Microsoft.Extensions.AI abstraction in one sentence.");
```

---

## 🦙 Ollama (Local or Cloud)

OllamaSharp is already a Delibera dependency, so no extra package is needed — just use the
`OllamaProvider` adapter and grab its `IChatClient`.

### Local Ollama

```csharp
using Delibera.Core.Extensions;
using Delibera.Core.Providers.LLM;

using var ollama = new OllamaProvider("http://localhost:11434");
IChatClient chatClient = ollama.AsChatClient();

var llmProvider = new ChatClientLLMProvider(chatClient, "Ollama Local");

string response = await llmProvider.ChatAsync(
    "llama3.2",
    "You are a helpful assistant.",
    "What is the capital of France?",
    0.7f);

Console.WriteLine(response);
```

### Ollama Cloud (with API key)

```csharp
using var ollama = new OllamaProvider("https://api.ollama.com", apiKey: "YOUR_OLLAMA_CLOUD_KEY");
var llmProvider = ollama.AsChatClient().AsLLMProvider("Ollama Cloud");
```

---

## 🛠️ OpenAI-Compatible Servers (LM Studio, vLLM, LocalAI)

These servers expose an OpenAI-compatible HTTP API, so the same
`Microsoft.Extensions.AI.OpenAI` package works — point it at the local URL.

```csharp
using Microsoft.Extensions.AI;
using OpenAI;

// LM Studio typically serves OpenAI-compatible API at http://localhost:1234/v1
var client = new OpenAIClient(
    new OpenAIAuthentication("lm-studio"),                // any non-empty string key works
    new System.Net.Http.HttpClient { BaseAddress = new Uri("http://localhost:1234/v1") });

IChatClient chatClient = client.GetChatClient("local-model").AsIChatClient();
var llmProvider = new ChatClientLLMProvider(chatClient, "LM Studio");
```

---

## 🧠 Streaming (ChatStreamAsync)

Because `ChatClientLLMProvider` is backed by an `IChatClient`, it delivers true
token-by-token streaming:

```csharp
await foreach (var chunk in llmProvider.ChatStreamAsync(
                  "gpt-4o-mini",
                  "You are a concise assistant.",
                  "In one sentence, what is Microsoft.Extensions.AI?"))
{
    Console.Write(chunk);
}
```

---

## 🧮 Embeddings (IEmbeddingGenerator → IEmbeddingProvider)

The same interop works for embeddings, so RAG pipelines can use any M.E.AI embedding backend:

```csharp
using Microsoft.Extensions.AI;
using OpenAI;
using Delibera.Core.Extensions;

var openAi = new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);

IEmbeddingGenerator<string, Embedding<float>> generator =
    openAi.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator();

var embeddingProvider = generator.AsEmbeddingProvider("text-embedding-3-small");

float[] vector = await embeddingProvider.EmbedAsync("Delibera deliberates.");
Console.WriteLine($"Dimensions: {vector.Length}"); // 1536 for text-embedding-3-small
```

For Ollama embeddings, use the built-in helper:

```csharp
using var ollama = new OllamaProvider("http://localhost:11434");
var embeddings = ollama.AsEmbeddingGenerator().AsEmbeddingProvider("nomic-embed-text");
```

---

## ↩️ Reverse Bridge — Expose a Delibera Provider as IChatClient

Already have an `ILLMProvider` and want to drop it into a Microsoft.Extensions.AI
middleware pipeline (caching, telemetry, function invocation)? Use `AsChatClient`:

```csharp
using Delibera.Core.Extensions;

using var ollama = new OllamaProvider("http://localhost:11434");
IChatClient chatClient = ollama.AsChatClient()
    .WithMiddleware(enableFunctionInvocation: true);

// Now `chatClient` is a standard Microsoft.Extensions.AI client usable by any
// library that consumes IChatClient — while still talking to Ollama underneath.
```

If the provider is already a `ChatClientLLMProvider`, `AsChatClient()` returns its
underlying client directly (no extra wrapping layer).

---

## 🏥 Provider Introspection

`ChatClientLLMProvider` implements the full `ILLMProvider` surface:

| Method                       | Behaviour                                                                                      |
| ---------------------------- | ---------------------------------------------------------------------------------------------- |
| `IsAvailableAsync`           | Returns `true` while the client is not disposed (the M.E.AI abstraction has no health-check).   |
| `ListModelsAsync`            | Returns the default model id from the client metadata, or an empty list when unknown.          |
| `GetModelCapabilitiesAsync`  | Falls back to the static `ModelContextWindowRegistry` (the M.E.AI abstraction has no caps API). |
| `ChatAsync`                  | One-shot chat via `IChatClient.GetResponseAsync`.                                              |
| `ChatStreamAsync`            | True token streaming via `IChatClient.GetStreamingResponseAsync`.                              |

```csharp
var llmProvider = new ChatClientLLMProvider(chatClient, "OpenAI");

Console.WriteLine(await llmProvider.IsAvailableAsync());             // True
Console.WriteLine(string.Join(", ", await llmProvider.ListModelsAsync())); // gpt-4o
var caps = await llmProvider.GetModelCapabilitiesAsync("gpt-4o");     // may be null
```

---

## 🧱 DI Registration

Register an `IChatClient`-backed provider through `ProviderFactory` so it participates in
the Delibera DI container:

```csharp
using Delibera.Core.DependencyInjection;
using Delibera.Core.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;

var builder = Host.CreateApplicationBuilder();

builder.Services.AddChatClient(services =>
        new OpenAIClient(builder.Configuration["OPENAI_API_KEY"]!)
            .GetChatClient("gpt-4o-mini")
            .AsIChatClient())
    .UseFunctionInvocation()
    .UseLogging();

builder.Services.AddDelibera(builder.Configuration, "Delibera");
// AddDeliberaChatClient(...) wires the registered IChatClient into the council pipeline.

var app = builder.Build();
var executor = app.Services.GetRequiredService<ICouncilBuilder>()
    .AddMember("gpt-4o-mini", /* resolved from ILLMProviderFactory */, "Analyst")
    .Build();
```

Or wrap it manually and add it to the factory cache:

```csharp
using var factory = new ProviderFactory();
IChatClient chatClient = new OpenAIClient(key).GetChatClient("gpt-4o-mini").AsIChatClient();
ChatClientLLMProvider provider = factory.CreateFromChatClient("openai", chatClient, "OpenAI");
```

---

## 📋 Capabilities & Limitations

**Advantages**

- ✅ **Flexibility** — works with any `IChatClient` implementation, enabling seamless
  integration with multiple AI backends.
- ✅ **Middleware support** — leverages Microsoft.Extensions.AI middleware (function
  invocation, caching, telemetry, logging) via `WithMiddleware`.
- ✅ **Streaming** — true token-by-token streaming via `ChatStreamAsync`.
- ✅ **Bidirectional** — `AsLLMProvider` / `AsChatClient` / `AsEmbeddingProvider` let you
  move freely between Delibera and M.E.AI worlds.

**Considerations**

- ⚠️ **Model enumeration** — the Microsoft.Extensions.AI abstraction does not define a
  model-enumeration contract, so `ListModelsAsync` may return limited results (only the
  default model id, when known).
- ⚠️ **Capabilities** — model capabilities are not universally defined and rely on the
  static `ModelContextWindowRegistry` or per-provider overrides.
- ⚠️ **Health check** — `IsAvailableAsync` reports client liveness only; concrete
  transport failures surface on the first `ChatAsync` call.

---

## 🖥️ Runnable Example

A complete, runnable console example lives at
[`src/Delibera.ConsoleApp/Examples/ChatClientLLMProviderExample.cs`](../src/Delibera.ConsoleApp/Examples/ChatClientLLMProviderExample.cs).

```bash
cd src/Delibera.ConsoleApp
dotnet run -- --chatclient
```

It demonstrates: obtaining an `IChatClient`, composing middleware, adopting as an
`ILLMProvider`, provider introspection, `ChatAsync`, `ChatStreamAsync`, the reverse
`AsChatClient` bridge, and embeddings — using a local Ollama server (the OpenAI variant
is shown in the file's comments).

---

## 📚 Further Reading

- [Microsoft.Extensions.AI documentation](https://learn.microsoft.com/dotnet/ai/ichatclient)
- [Microsoft.Extensions.AI on NuGet](https://www.nuget.org/packages/Microsoft.Extensions.AI)
- [Delibera README](../README.md)