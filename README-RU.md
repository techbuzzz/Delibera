<div align="center">

<img src="img/delibera-horizontal-1920x480.png" alt="Delibera" width="640">

# Delibera

### ⚖️ Продуманные решения с помощью ИИ

**Коллективное принятие решений через структурированное обсуждение ИИ — с RAG, pgvector, Knowledge Keeper, 🛠️ Operator (MCP-инструменты), Chairman, 🔥 сжатием контекста, 💉 Dependency Injection и 📋 журналированием выполнения**

[![NuGet](https://img.shields.io/nuget/v/Delibera.Core.svg)](https://www.nuget.org/packages/Delibera.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-10B981.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-1F2937.svg)](https://dotnet.microsoft.com)
[![C# 15](https://img.shields.io/badge/C%23-15.0--preview-239120.svg)](https://learn.microsoft.com/dotnet/csharp/)

🇬🇧 [English version (README.md)](README.md)

</div>

---

## 📖 Обзор

**Delibera** — это фреймворк на C# / .NET 10, который оркеструет **многомодельные обсуждения**
между LLM. Несколько моделей ИИ рассуждают над вопросом в течение структурированных раундов,
критикуют ответы друг друга, а **Chairman** (председатель) взвешивает аргументы, чтобы
синтезировать сбалансированный финальный вердикт — обогащённый **Knowledge Keeper** на базе
**Qdrant** или **PostgreSQL/pgvector** (RAG), с **интеллектуальным сжатием контекста** для
минимизации расхода токенов.

Название происходит от слова *deliberation* — тщательного взвешивания доказательств и точек зрения
перед принятием решения. Delibera привносит эту дисциплину в ИИ, помогая командам приходить к
**продуманным, хорошо обоснованным результатам**, а не к догадкам одной модели.

---

## ✨ Ключевые возможности

| Возможность                       | Описание                                                                          |
| --------------------------------- | --------------------------------------------------------------------------------- |
| **🏛️ Многомодельные советы**     | Оркестрация любого числа LLM-участников по структурированным раундам дебатов        |
| **⚖️ Синтез Chairman**            | Выделенный модератор открывает, регулирует и синтезирует финальный вердикт          |
| **📚 Knowledge Keeper (RAG)**     | Семантический поиск по раундам со структурированными ответами и цитированием         |
| **🛠️ Operator (MCP-инструменты)** | Микроагент, делегирующий задачи MCP-серверам (веб, файлы, Marp, Notion, …) по запросу в ходе дебатов |
| **🐘 Qdrant + pgvector**          | Подключаемые векторные хранилища — отдельная БД или ваш существующий PostgreSQL      |
| **🗜️ Сжатие контекста**          | 4 стратегии (Semantic, Deduplication, Summarization, Hybrid) экономят 30–70% токенов |
| **💉 Dependency Injection**       | Расширение `AddDelibera()` для `IServiceCollection` с полной привязкой опций         |
| **📋 Журналирование выполнения**  | Модель `ExecutionLog` с `LogLevel` — события Chairman, KK, сжатия и участников        |
| **📁 Раздельный вывод файлов**    | Экспорт `result.md`, `statistics.md` и `logs.md` по отдельности                     |
| **🔌 Interface-First**            | Чистые абстракции для провайдеров, фабрик, билдеров и исполнителей                   |
| **🧱 Современный C# 15 (preview)** | Построено на .NET 10 с `LangVersion=preview`, file-scoped namespaces, records, span/SIMD горячие пути |

---

## 📑 Содержание

- [Быстрый старт](#-быстрый-старт)
  - [Требования и модели](#требования-и-модели)
  - [Установка](#установка)
  - [Минимальный пример](#минимальный-пример)
  - [Запуск](#запуск)
- [Dependency Injection](#-dependency-injection)
- [Operator (MCP-инструменты)](#️-operator-mcp-инструменты)
- [Сжатие контекста](#️-сжатие-контекста)
- [Интеграция RAG](#-интеграция-rag)
- [Стратегии дебатов](#️-стратегии-дебатов)
- [Структура выходных файлов](#-структура-выходных-файлов)
- [Примеры ConsoleApp](#-примеры-consoleapp)
- [Установка и сборка](#️-установка-и-сборка)
- [Архитектура](#️-архитектура)
- [Участие в разработке](#-участие-в-разработке)
- [Лицензия](#-лицензия)

---

## 🚀 Быстрый старт

### Требования и модели

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (≥ 10.0.301) — проект нацелен на
  `net10.0` и собирается с `LangVersion=preview` для включения возможностей **C# 15**. См.
  [docs/NET10-Upgrade-RU.md](docs/NET10-Upgrade-RU.md) для полных заметок о миграции.
- Запущенный экземпляр [Ollama](https://ollama.com) — локально (`ollama serve`) или
  [Ollama Cloud](https://ollama.com/cloud) (только API-ключ, без установки).
- Минимальный набор моделей, перечисленный ниже.
- **Опционально — для роли Operator:** [Node.js + npx](https://nodejs.org) для запуска MCP-серверов
  (например, `@playwright/mcp`, `@marp-team/marp-cli`). См. [Operator (MCP-инструменты)](#️-operator-mcp-инструменты).

#### 🟢 Минимальный — для небольших дебатов (≈ 2 ГБ всего)

Подходит для smoke-тестов, слабого железа и быстрых запусков из CLI.

| Назначение          | Модель              | Размер  | Команда загрузки                   |
| ------------------- | ------------------- | ------- | ---------------------------------- |
| Участник совета     | `llama3.2:1b`       | 1.3 ГБ  | `ollama pull llama3.2:1b`          |
| Участник совета     | `qwen2.5:1.5b`      | 1.1 ГБ  | `ollama pull qwen2.5:1.5b`         |
| Эмбеддинги (RAG)    | `nomic-embed-text`  | 274 МБ  | `ollama pull nomic-embed-text`     |

#### 🟡 Стандартный — рекомендуется для большинства случаев (≈ 7 ГБ всего)

Хорошее качество рассуждений при низкой задержке. **Это набор по умолчанию, используемый по всему README.**

| Назначение          | Модель               | Размер  | Команда загрузки                    |
| ------------------- | -------------------- | ------- | ----------------------------------- |
| Участник совета     | `llama3.2:3b`        | 2.0 ГБ  | `ollama pull llama3.2:3b`           |
| Участник совета     | `qwen2.5:7b`         | 4.7 ГБ  | `ollama pull qwen2.5:7b`            |
| Эмбеддинги (RAG)    | `nomic-embed-text`   | 274 МБ  | `ollama pull nomic-embed-text`      |

#### 🔴 Высокопроизводительный — для продакшн-уровня дебатов (≈ 30+ ГБ)

Более тяжёлые локальные модели, рекомендуются на GPU с ≥ 24 ГБ VRAM или на Ollama Cloud.

| Назначение          | Модель                       | Размер    | Команда загрузки                              |
| ------------------- | ---------------------------- | --------- | --------------------------------------------- |
| Участник совета     | `llama3.1:8b`                | 4.9 ГБ    | `ollama pull llama3.1:8b`                     |
| Участник совета     | `qwen2.5:14b`                | 9.0 ГБ    | `ollama pull qwen2.5:14b`                     |
| Участник совета     | `mistral:7b`                 | 4.4 ГБ    | `ollama pull mistral:7b`                      |
| Chairman            | `qwen2.5:14b` *(или больше)* | 9.0 ГБ    | `ollama pull qwen2.5:14b`                     |
| Эмбеддинги (RAG)    | `nomic-embed-text`           | 274 МБ    | `ollama pull nomic-embed-text`                |

> 💡 **Ollama Cloud** использует те же имена моделей, но не требует места на локальном диске — нужен
> только API-ключ. См. [раздел конфигурации](#-dependency-injection) для его настройки.

### Установка

```bash
dotnet add package Delibera.Core
```

### Минимальный пример

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

> 📄 См. [docs/QuickStart-RU.md](docs/QuickStart-RU.md) для пошагового руководства.

### Запуск

```bash
dotnet run
```

Delibera проведёт структурированные многораундовые дебаты и запишет полную стенограмму и вердикт
председателя в файл `deliberation.md`.

---

## 💉 Dependency Injection

Зарегистрируйте все сервисы Delibera одной строкой:

```csharp
using Delibera.Core.DependencyInjection;

// Вариант A: С привязкой конфигурации (привязывает секцию "Delibera")
services.AddDelibera(configuration, "Delibera");

// Вариант B: С делегатом опций
services.AddDelibera(options =>
{
    options.Strategy = "Standard";
    options.MaxRounds = 4;
    options.Temperature = 0.7f;
    options.Compression.Enabled = true;
    options.Compression.Strategy = "Hybrid";
    options.Compression.TargetRatio = 0.5;
});

// Вариант C: Только значения по умолчанию
services.AddDelibera();
```

Резолвит из DI следующие интерфейсы:

| Интерфейс             | Реализация           | Время жизни |
| --------------------- | -------------------- | ----------- |
| `ILLMProviderFactory` | `ProviderFactory`    | Singleton   |
| `IRagProviderFactory` | `RagProviderFactory` | Singleton   |
| `ICompressionFactory` | `CompressionService` | Singleton   |
| `ICouncilBuilder`     | `CouncilBuilder`     | Transient   |

### Конфигурация (`appsettings.json`)

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

Чтобы использовать **Ollama Cloud**, задайте `Providers:DefaultEndpoint` равным
`https://api.ollama.com` и поместите ключ в `Providers:ApiKey` (или `OllamaCloud:ApiKey` в секции
`DeliberaApp`, используемой консольным приложением — см. [Примеры ConsoleApp](#-примеры-consoleapp)).

---

## 🛠️ Operator (MCP-инструменты)

**Operator** — это лёгкий микроагент, который соединяет совет с внешним миром через серверы
[**MCP (Model Context Protocol)**](https://modelcontextprotocol.io). Он предоставляет участникам
дебатов любые инструменты, которые дают эти серверы — веб-навигацию, доступ к файловой системе,
генерацию Marp-презентаций, Notion, PostgreSQL и т. д.

**Как это работает**

1. Operator подключается к одному или нескольким MCP-серверам и обнаруживает их инструменты при
   `InitializeAsync`.
2. Участникам сообщается (в их системном промпте), что умеет Operator, и они могут делегировать
   задачу в **любой момент** дебатов, написав маркер в своём сообщении:

   ```
   [[OPERATOR: открой https://modelcontextprotocol.io и кратко перескажи, что такое MCP]]
   ```

3. Operator интерпретирует запрос своей **собственной (более дешёвой) LLM-моделью**, выбирает и
   вызывает нужные MCP-инструменты, интерпретирует результаты и возвращает краткий ответ, который
   внедряется в следующий раунд.
4. Если совет использует **сжатие контекста**, Operator может переиспользовать ту же стратегию для
   сжатия больших выводов инструментов перед их возвратом в дебаты. Его `DisposeAsync` —
   это `ValueTask` (паттерн `IAsyncDisposable` из .NET 10), поэтому MCP-клиенты освобождаются без
   аллокации `Task`.

Все взаимодействия с Operator записываются по раундам и отображаются в финальном Markdown-отчёте в
блоке **🛠️ Operator Interactions**.

### Настройка MCP-серверов

```csharp
using Delibera.Core.Models;

var servers = new[]
{
    // stdio-транспорт — запускает локальный процесс MCP-сервера
    McpServerConfig.Stdio(
        name: "browser",
        command: "npx",
        arguments: new[] { "-y", "@playwright/mcp@latest", "--headless" }),

    McpServerConfig.Stdio(
        name: "marp",
        command: "npx",
        arguments: new[] { "-y", "@marp-team/marp-cli", "--server", "./out" }),

    // …или HTTP/SSE-транспорт для удалённого MCP-сервера
    // McpServerConfig.Http(
    //     name: "remote",
    //     endpoint: "https://my-mcp-host.example.com/mcp",
    //     additionalHeaders: new Dictionary<string, string> { ["Authorization"] = "Bearer <token>" }),
};
```

| Сервер       | Транспорт | Команда запуска                                 | Что даёт                                              |
| ------------ | --------- | ----------------------------------------------- | ---------------------------------------------------- |
| 🌐 `browser` | stdio     | `npx -y @playwright/mcp@latest --headless`      | Навигация по сайтам, чтение страниц, клики, скриншоты |
| 🎯 `marp`    | stdio     | `npx -y @marp-team/marp-cli --server <dir>`     | Генерация презентаций (HTML/PDF/PPTX) из Markdown     |

### Быстрое использование (внутри совета)

```csharp
using Delibera.Core.Council;
using Delibera.Core.Providers.LLM;

var ollama = new OllamaProvider("http://localhost:11434");

var council = new CouncilBuilder()
    .AddMember("llama3.2:3b", ollama, "Optimist")
    .AddMember("qwen2.5:7b",  ollama, "Skeptic")
    .SetChairman(Chairman.CreateStandard("qwen2.5:7b", ollama))
    // Operator использует свою более дешёвую модель; reuseCompression разделяет компрессор совета
    .WithOperator("llama3.2:3b", ollama, servers, reuseCompression: true)
    .WithStandardDebate()
    .WithUserPrompt("Research the latest .NET 10 features and prepare a short summary.")
    .WithMaxRounds(4)
    .Build()
    .ExecuteAsync();
```

Предпочитаете создать `Operator` самостоятельно? Передайте готовый экземпляр:

```csharp
using Delibera.Core.Council;
using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Core.Providers.Mcp;

var @operator = new Operator(
    new CouncilMember("llama3.2:3b", ollama, "Operator"),
    new IMcpClient[] { new McpClientAdapter(servers[0]), new McpClientAdapter(servers[1]) },
    compressor: null,            // необязательный IContextCompressor
    compressionOptions: null);   // необязательные CompressionOptions

var council = new CouncilBuilder()
    /* …участники… */
    .WithOperator(@operator)
    .Build();
```

### Прямое использование (без совета)

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

Настройте Operator декларативно в `appsettings.json` в секции `Delibera:Operator`:

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

> ▶️ Полный рабочий пример находится в
> [`OperatorMcpToolsExample.cs`](src/Delibera.ConsoleApp/Examples/OperatorMcpToolsExample.cs).
> Запустите его командой `dotnet run --project src/Delibera.ConsoleApp -- --operator-mcp`.
> Полное техническое описание см. в [docs/NET10-Upgrade-RU.md](docs/NET10-Upgrade-RU.md).

---

## 🗜️ Сжатие контекста

Автоматически сжимайте контекст между раундами обсуждения — экономьте **30–70% токенов** без потери смысла.

| Стратегия         | Как работает                                              | Лучше всего для                  |
| ----------------- | -------------------------------------------------------- | -------------------------------- |
| **Semantic**      | Эмбеддинг предложений, ранжирование по релевантности, топ-N | Большие контексты знаний          |
| **Deduplication** | Удаляет семантически похожие предложения между участниками | Многомодельные дебаты с пересечениями |
| **Summarization** | LLM создаёт краткое резюме, сохраняя ключевые факты        | Максимальная степень сжатия       |
| **Hybrid**        | Конвейер Dedup → Semantic → Summarize                     | Лучшее общее качество             |
| **None**          | Без изменений (когда отключено)                           | Отладка                           |

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

### Конвейер сжатия

```
IContextCompressor
├── SemanticCompressor        ← Ранжирование предложений на основе эмбеддингов
├── DeduplicationCompressor   ← Удаление дубликатов по схожести
├── SummarizationCompressor   ← Резюмирование с помощью LLM
├── HybridCompressor          ← Многоэтапный конвейер (Dedup → Semantic → Summarize)
└── PassThroughCompressor     ← Без операций (когда отключено)

CompressionFactory            ← Статическая фабрика (Create по enum или строке)
CompressionService            ← DI-дружественная обёртка над CompressionFactory
CompressionCache              ← LRU-кэш с ключами SHA-256
TokenCounter                  ← Эвристическая оценка токенов
```

---

## 📚 Интеграция RAG

Используйте выделенный экземпляр **Qdrant** или вашу существующую базу **PostgreSQL/pgvector** в
качестве векторного хранилища.

```csharp
using Delibera.Core.Council;
using Delibera.Core.Models;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Providers.RAG;

var ollama = new OllamaProvider("http://localhost:11434");
var embeddings = new OllamaEmbeddingProvider(ollama, "nomic-embed-text");

// pgvector — просто добавьте строку подключения
var ragFactory = new RagProviderFactory();
var rag = ragFactory.CreatePgVector(
    embeddings,
    "Host=localhost;Database=council_vectors;Username=postgres;Password=postgres");

await rag.IndexDocumentAsync("my_collection", documentText);
var results = await rag.SearchAsync("my_collection", "query", limit: 5);

// Подключаем к Knowledge Keeper
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

Затем Knowledge Keeper присоединяется к совету через `WithKnowledgeKeeper(...)` — см.
[пример RAG](src/Delibera.ConsoleApp/Examples/RagExample.cs) для полного рабочего демо.

---

## 🗣️ Стратегии дебатов

| Стратегия             | Поток                                                   | Сценарий использования  |
| --------------------- | ------------------------------------------------------- | ----------------------- |
| **StandardDebate**    | Initial → Critique → Improved → Verdict                 | Общий анализ            |
| **CritiqueDebate**    | Position → Attack → Defence → Judge                     | Проверка гипотез        |
| **ConsensusDebate**   | Perspectives → Common Ground → Consensus → Facilitator  | Поиск оптимального решения |

Каждая стратегия реализована как `IDebateStrategy` — см.
[`src/Delibera.Core/Debate/`](src/Delibera.Core/Debate/) для полного исходного кода.

### Паттерны проектирования

| Паттерн             | Использование                                                             |
| ------------------- | ------------------------------------------------------------------------- |
| **Factory**         | `ProviderFactory`, `RagProviderFactory`, `CompressionFactory`, `Chairman` |
| **Strategy**        | `IDebateStrategy`, `IContextCompressor`                                   |
| **Builder**         | Fluent API `CouncilBuilder`                                               |
| **Template Method** | Абстрактный базовый класс `DebateScenario`                                |
| **Cache**           | `CompressionCache` с ключами SHA-256                                      |
| **Observer**        | Событие `OnRoundCompleted` у `CouncilExecutor`                            |

---

## 📁 Структура выходных файлов

Каждое обсуждение можно экспортировать как один файл или как три отдельных Markdown-документа:

```csharp
var result = await executor.ExecuteAsync();

// Сохранить в 3 отдельных файла
var (resultPath, statsPath, logsPath) = await result.SaveAllAsync("./output");
// Создаёт: debate_20260604_120000_result.md
//          debate_20260604_120000_statistics.md
//          debate_20260604_120000_logs.md

// Или сохранить по отдельности
await result.SaveToMarkdownAsync("result.md");
await result.SaveStatisticsAsync("statistics.md");
await result.SaveLogsAsync("logs.md");
```

| Файл              | Содержимое                                                                    |
| ----------------- | ----------------------------------------------------------------------------- |
| `*_result.md`     | Полная стенограмма обсуждения, раунды и финальный вердикт председателя         |
| `*_statistics.md` | Статистика использования токенов с разбивкой по раундам                        |
| `*_logs.md`       | Журналы выполнения (`ExecutionLog`) для Chairman, KK, сжатия и участников      |

---

## 💻 Примеры ConsoleApp

В репозитории есть [`Delibera.ConsoleApp`](src/Delibera.ConsoleApp/) — запускаемый демо-проект,
который задействует каждую возможность. Запускайте его из корня репозитория:

```bash
# Клонировать репозиторий
git clone https://github.com/delibera/Delibera.git
cd Delibera/src/Delibera.ConsoleApp

# Запустить конкретный пример
dotnet run -- --di                 # Dependency Injection
dotnet run -- --separate-files     # Сохранить result.md, statistics.md, logs.md
dotnet run -- --compression        # Демо сжатия контекста
dotnet run -- --multiprovider      # Совет с несколькими провайдерами (cloud + local)
dotnet run -- --rag                # RAG на базе Qdrant с Knowledge Keeper
dotnet run -- --pgvector           # RAG на базе pgvector
dotnet run -- --operator           # Основы роли Operator (MCP-инструменты)
dotnet run -- --operator-mcp       # 🆕 Operator с MCP-серверами browser + Marp

# Или запустить полное демо по умолчанию (читает appsettings.json)
dotnet run
```

Консольное приложение читает [`appsettings.json`](src/Delibera.ConsoleApp/appsettings.json), который
использует секцию `DeliberaApp` — по умолчанию она указывает на Ollama Cloud и показывает, как
подключить несколько провайдеров, RAG и сжатие в одном месте.

---

## 🛠️ Установка и сборка

### Клонирование и сборка

```bash
git clone https://github.com/delibera/Delibera.git
cd Delibera

# Собрать всё решение
dotnet build --configuration Release

# Запустить консольное демо
cd src/Delibera.ConsoleApp
dotnet run
```

### Инфраструктура одной командой (Docker Compose)

Поднимите **Qdrant + PostgreSQL/pgvector** за один шаг. (Ollama намеренно не включена —
[установите её нативно](https://ollama.com/download), чтобы GPU-драйвер использовался напрямую.)

```bash
# Из корня репозитория
docker compose up -d

# В другом терминале запустите консольное приложение
cd src/Delibera.ConsoleApp
dotnet run
```

Compose-стек предоставляет:

| Сервис        | URL                        | Назначение                    |
| ------------- | -------------------------- | ----------------------------- |
| Qdrant (REST) | `http://localhost:6333`    | UI / REST векторного хранилища |
| Qdrant (gRPC) | `localhost:6334`           | gRPC-клиент (используется Delibera) |
| PostgreSQL    | `localhost:5432`           | RAG-хранилище pgvector        |

Учётные данные по умолчанию: `postgres` / `postgres`, база `council_vectors`.

> Если эти сервисы уже запущены нативно, просто пропустите `docker compose` и направьте консольное
> приложение на них — `appsettings.json` по умолчанию использует `localhost`.

### Альтернатива вручную (по одному контейнеру)

```bash
# Запуск с Qdrant (Docker)
docker run -d -p 6333:6333 -p 6334:6334 qdrant/qdrant

# Запуск с pgvector (Docker)
docker run -d -p 5432:5432 -e POSTGRES_PASSWORD=postgres pgvector/pgvector:pg16

# Затем включите расширение pgvector
docker exec -it <container> psql -U postgres -d council_vectors -c "CREATE EXTENSION IF NOT EXISTS vector;"
```

### NuGet-зависимости

| Пакет                    | Назначение                       |
| ------------------------ | -------------------------------- |
| `OllamaSharp`            | Клиент API Ollama                |
| `Qdrant.Client`          | gRPC-клиент векторной БД Qdrant  |
| `Npgsql`                 | ADO.NET-провайдер PostgreSQL     |
| `Pgvector`               | Поддержка типов pgvector для Npgsql |
| `ModelContextProtocol`   | MCP-клиент для роли Operator     |
| `Microsoft.Extensions.*` | Конфигурация, DI и Options       |

---

## 🏛️ Архитектура

```
Delibera.Core
├── Council/              ← CouncilBuilder, CouncilExecutor, Chairman, KnowledgeKeeper, Operator
├── Debate/               ← StandardDebate, CritiqueDebate, ConsensusDebate
├── Compression/          ← Semantic / Deduplication / Summarization / Hybrid
├── Providers/
│   ├── LLM/              ← OllamaProvider, OllamaEmbeddingProvider
│   ├── RAG/              ← QdrantRagProvider, PgVectorRagProvider
│   └── Mcp/              ← McpClientAdapter (Operator ↔ MCP-серверы)
├── DependencyInjection/  ← AddDelibera() + CouncilOptions
├── Knowledge/            ← MarkdownKnowledgeBase
├── Models/               ← CouncilMember, DebateResult, DebateRound, TokenStatistics, ...
└── Interfaces/           ← ILLMProvider, IRagProvider, IContextCompressor, IOperator, IMcpClient, ...
```

---

## 🤝 Участие в разработке

Мы приветствуем вклад! Пожалуйста, прочитайте [CONTRIBUTING.md](CONTRIBUTING.md) для рекомендаций по
настройке окружения разработки, стандартам кодирования и процессу pull-request.

---

## 📄 Лицензия

Delibera распространяется под [лицензией MIT](LICENSE).

Copyright © 2026 Delibera Project.

---

<div align="center">

**⚖️ Delibera — Продуманные решения с помощью ИИ**

*Создано с заботой о коллективном интеллекте на базе ИИ*

</div>
