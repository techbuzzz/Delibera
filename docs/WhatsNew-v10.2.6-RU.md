# Delibera v10.2.6 — Что нового

> **Статус:** ✅ Выпущено — июль 2026  
> **9 из 10 запланированных фич доставлено** (F-06 Multi-Modal перенесён в v10.2.7 по запросу пользователя).  
> **259 модульных тестов проходят** (со 105 в v10.2.5).  
> **Без breaking changes** — каждая новая возможность opt-in через дополнительные методы билдера.

Этот документ — пользовательское руководство по девяти новым возможностям
Delibera v10.2.6. Технический roadmap см. в [docs/v10.2.6.md](v10.2.6.md).
Полный changelog — в [CHANGELOG.md](../CHANGELOG.md).

---

## Содержание

- [F-08 — OpenTelemetry-стиль наблюдаемости](#f-08--opentelemetry-стиль-наблюдаемости)
- [F-10 — Quick Wins Bundle](#f-10--quick-wins-bundle)
- [F-07 — Шаблоны дебатов](#f-07--шаблоны-дебатов)
- [F-01 — Асинхронный потоковый совет](#f-01--асинхронный-потоковый-совет)
- [F-09 — Адаптивная смена стратегии](#f-09--адаптивная-смена-стратегии)
- [F-02 — Подключаемый движок голосования](#f-02--подключаемый-движок-голосования)
- [F-05 — Структурированный вывод](#f-05--структурированный-вывод)
- [F-03 — Персистентность и возобновление дебатов](#f-03--персистентность-и-возобновление-дебатов)
- [F-04 — Память агентов](#f-04--память-агентов)
- [Демо-записи ConsoleApp](#демо-записи-consoleapp)

---

## F-08 — OpenTelemetry-стиль наблюдаемости

Production-grade наблюдаемость для каждого совета — спаны для round loop,
метрики для длительности, токенов и степени сжатия, с нулевыми накладными
расходами, когда слушатель не подключён.

```csharp
var executor = new CouncilBuilder()
    .AddMember("llama3.2", llm, "Analyst")
    .AddMember("qwen2.5", llm, "Critic")
    .WithStandardDebate()
    .WithUserPrompt("Microservices vs monolith?")
    .WithMaxRounds(3)
    .WithTelemetry()  // ← F-08
    .Build();

await executor.ExecuteAsync();

// Подключите к своему OpenTelemetry pipeline:
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("Delibera.Council").AddJaegerExporter())
    .WithMetrics(b => b.AddMeter("Delibera.Metrics").AddPrometheusExporter());
```

**Иерархия спанов:**

```
delibera.council.execute
├── delibera.council.round          (per round, tag: round_number)
│   ├── delibera.member.respond     (per participant, tag: member_name)
│   ├── delibera.rag.query          (если подключён Knowledge Keeper)
│   ├── delibera.compression        (если включено сжатие)
│   └── delibera.operator.execute   (per Operator task)
└── delibera.chairman.synthesize
```

**Метрики:**
- `delibera.debate.duration` (Histogram, ms)
- `delibera.round.duration` (Histogram, ms, tag: `round_number`)
- `delibera.tokens.total` (Counter, tags: `member_name`, `direction`)
- `delibera.compression.ratio` (Gauge)
- `delibera.debates.completed` (Counter, tags: `strategy`, `success`)

Конфигурация через `appsettings.json` секцию `Delibera:Telemetry` или
делегат `WithTelemetry(Action<TelemetryOptions>)`.

---

## F-10 — Quick Wins Bundle

Пять небольших, но высокоценных фич, улучшающих DX и операционную надёжность.

### F-10a — HTML-экспорт

```csharp
// Автономный HTML с inline CSS, сворачиваемые <details> для раундов
var html = result.ToHtml(new HtmlExportOptions
{
    Theme = HtmlTheme.Dark,        // или Light
    CollapsibleRounds = true
});
await result.SaveToHtmlAsync("./result.html");
```

### F-10b — Таймаут дебатов

```csharp
.WithTimeout(TimeSpan.FromMinutes(10))  // общий бюджет wall-clock
```

Таймаут линкуется с `CancellationToken` вызывающего кода через
`CancellationTokenSource.CreateLinkedTokenSource` и оба источника
освобождаются в `finally`.

### F-10c — Пресеты Persona

```csharp
.AddMember("llama3.2", llm, "Devil's Advocate", Persona.DevilsAdvocate)
.AddMember("qwen2.5", llm, "Risk Manager",     Persona.RiskManager)
.AddMember("mistral", llm, "Pragmatist",       Persona.Pragmatist)
```

Шесть встроенных пресетов: `Expert`, `DevilsAdvocate`, `CautiousOptimist`,
`DataDrivenAnalyst`, `RiskManager`, `Pragmatist`. Резолвится через
`Persona.Resolve(name)`.

### F-10d — Benchmark / сравнение моделей

```csharp
var benchmark = new CouncilBenchmark()
    .AddConfiguration("Small", b => b.AddMember("llama3.2:1b", llm, "A"))
    .AddConfiguration("Standard", b => b.AddMember("llama3.2:3b", llm, "A"))
    .WithQuestion("Microservices or monolith?")
    .WithMaxRounds(3);

var report = await benchmark.RunAsync();
await report.SaveComparisonAsync("./benchmark.md");
```

Прогоняет конфигурации последовательно (детерминированно, без перекоса
из-за rate-limit), фиксирует ошибки по-entry, рендерит side-by-side
сравнение с вердиктами, использованием токенов и таблицей задержек.

### F-10e — Лимит участников

```csharp
.WithParticipantLimit(maxParticipants: 5)  // бросает исключение на Build() при превышении
```

Защитный гард для динамических DI-driven конфигураций, где список
участников формируется в рантайме.

---

## F-07 — Шаблоны дебатов

Шесть готовых к запуску конфигураций совета для типовых сценариев.

```csharp
var executor = DebateTemplate.ArchitectureReview
    .WithProvider(llm)
    .WithQuestion("Should we adopt event-driven architecture?")
    .WithMaxRounds(4)
    .Build();
```

**Доступные шаблоны:**

| Шаблон | Участники | Стратегия | Сценарий |
| --- | --- | --- | --- |
| `ArchitectureReview` | Architect, SecurityExpert, PerfEngineer | CritiqueDebate | Проектирование систем |
| `RiskAssessment` | Optimist, Pessimist, Realist, RiskManager | ConsensusDebate | Бизнес-риски |
| `CodeReview` | Reviewer, Defender, QA, TechLead | CritiqueDebate | Анализ PR |
| `ProductDecision` | PM, TechLead, UXDesigner | StandardDebate | Приоритизация фич |
| `SecurityAudit` | RedTeam, BlueTeam, Auditor | CritiqueDebate | Моделирование угроз |
| `DataArchitecture` | DataEngineer, DBA, MLEngineer | ConsensusDebate | Data platform |

`DebateTemplate.Custom()` возвращает свежий `CouncilBuilder` для полного
контроля. Шаблоны можно уточнять тем же fluent API, что и
`ICouncilBuilder` (`WithMaxRounds`, `WithTemperature`, `AddMember`,
`Advanced(...)`).

---

## F-01 — Асинхронный потоковый совет

Отдавайте каждый `DebateRound` по мере завершения — идеально для
ASP.NET Core SSE, WebSocket, Blazor и CLI live output.

```csharp
// CLI live output
await foreach (var round in executor.StreamDebateAsync(ct))
{
    Console.WriteLine($"[Round {round.RoundNumber}/{round.Total}] {round.RoundName}");
    foreach (var (member, response) in round.Responses)
        Console.WriteLine($"  {member}: {Truncate(response, 200)}");
}

// ASP.NET Core SSE
app.MapGet("/debate/stream", async (HttpContext ctx, ICouncilExecutor executor) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    await foreach (var round in executor.StreamDebateAsync(ctx.RequestAborted))
    {
        var json = JsonSerializer.Serialize(round);
        await ctx.Response.WriteAsync($"data: {json}\n\n");
        await ctx.Response.Body.FlushAsync();
    }
});
```

`DebateRound` получает свойства `Total` (int?) и `IsFinal` (bool) для
progress-UI. `ICouncilExecutor.LastStreamedResult` предоставляет доступ
к агрегированному `DebateResult` (включая логи и статистику токенов)
после завершения стрима. `ExecuteAsync` остаётся полностью обратно
совместимым.

---

## F-09 — Адаптивная смена стратегии

Подменяйте стратегию дебатов на лету при стагнации обсуждения.

```csharp
var selector = new AdaptiveStrategySelector
{
    Initial = new StandardDebate(),
    OnStalemate = new CritiqueDebate(),
    StagnationThreshold = 2,        // подряд идущих раундов с низким разнообразием
    StagnationScore = 0.3          // cutoff разнообразия (0.0-1.0)
};

var executor = new CouncilBuilder()
    .AddMember(/*...*/)
    .WithAdaptiveStrategy(selector)  // ← F-09
    .Build();
```

Когда embedding-провайдер недоступен, селектор использует fallback на
text-similarity Левенштейна. `DebateRound.StrategyUsed` записывает, какая
стратегия произвела каждый раунд, для аудита. Кастомные селекторы можно
реализовать через `IStrategySelector` для эвристик стагнации на основе
использования токенов, error rate или внешних сигналов.

> **Примечание:** Полная подмена стратегии в полёте требует, чтобы
> стратегии поддерживали отмену по раундам (будущее расширение). Текущая
> реализация записывает решение о смене в логи и проставляет
> `StrategyUsed` на результате; сама смена происходит на границах
> стратегии.

---

## F-02 — Подключаемый движок голосования

Замените синтез Chairman на структурированный подсчёт среди участников —
верифицируемый след решения для комплаенса, риск-комитетов и
архитектурных бордов.

```csharp
var votingStrategy = new WeightedVotingStrategy
{
    MemberWeights = { ["SecurityExpert"] = 2.0, ["Architect"] = 1.5 }
};

var executor = new CouncilBuilder()
    .AddMember("architect",       llm, "Architect")
    .AddMember("security-expert", llm, "SecurityExpert")
    .AddMember("perf-engineer",   llm, "PerfEngineer")
    .WithVotingChairman("qwen2.5", llm, votingStrategy)  // ← F-02
    .WithUserPrompt("Should we adopt microservices?")
    .WithMaxRounds(2)
    .Build();
```

**Встроенные стратегии:**

| Стратегия | Описание |
| --- | --- |
| `MajorityVotingStrategy` | Топ-вариант получает 1 балл за каждый бюллетень |
| `BordaCountVotingStrategy` | N-1 баллов за топ, N-2 за второй, ..., 0 за последний |
| `WeightedVotingStrategy` | Переопределения весов по участникам через `MemberWeights` |

После дебатов исполнитель просит каждого участника ранжировать опции,
обнаруженные в финальном раунде, подсчитывает бюллетени и рендерит
секцию 🗳️ **Voting Tally** в Markdown-выводе:

```markdown
## 🗳️ Voting Tally
**Method:** Weighted
**Winning option:** Microservices (score: 3.50)

| Option | Score |
|--------|------:|
| Microservices | 3.50 |
| Monolith | 1.50 |
```

Реализуйте `IVotingStrategy` для кастомных методов (Condorcet, STV, Borda
с усечёнными рангами и т.д.).

---

## F-05 — Структурированный вывод / JSON Schema

Получайте строго типизированный вердикт от Chairman — без парсинга
свободного текста.

```csharp
public sealed record ArchitectureDecision(
    string Recommendation,
    double Confidence,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Benefits,
    string Rationale);

var executor = new CouncilBuilder()
    .AddMember(/*...*/)
    .SetChairman("qwen2.5", llm)
    .WithStandardDebate()
    .WithUserPrompt("Migrate to microservices?")
    .WithMaxRounds(2)
    .WithStructuredOutput<ArchitectureDecision>()  // ← F-05
    .Build();

var (result, verdict) = await executor.ExecuteTypedAsync<ArchitectureDecision>();
// verdict — это типизированный ArchitectureDecision, а не строка для парсинга
```

JSON schema генерируется из C#-типа через .NET 10 `JsonSchemaExporter`,
добавляется к synthesis-промпту Chairman, и ответ десериализуется. Одна
автоматическая повторная попытка с корректирующим промптом выполняется
при ошибке десериализации. Реализуйте `IStructuredOutputSerializer` для
кастомного формата (YAML, XML и т.д.) или подключите
`Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<T>()` для
провайдеров с нативной JSON-schema-constrained декодированием.

---

## F-03 — Персистентность и возобновление дебатов

Переживайте сбои, рестарты и намеренные паузы, чекпоинтя дебат после
каждого раунда.

```csharp
var store = new FileDebateStore("./checkpoints", retentionDays: 30);

var executor = new CouncilBuilder()
    .AddMember(/*...*/)
    .WithUserPrompt("Long-running analysis question?")
    .WithMaxRounds(8)
    .WithPersistence(store)         // ← F-03: сохранять чекпоинт после каждого раунда
    .Build();

await executor.ExecuteAsync(ct);  // крахнулось посреди дебатов? Не проблема.

// Позже, в новом процессе:
var resumed = new CouncilBuilder()
    .AddMember(/*...*/)
    .WithUserPrompt("Long-running analysis question?")
    .WithMaxRounds(8)
    .WithPersistence(store)
    .ResumeFrom("debate-2026-07-06-abc123")  // ← F-03
    .Build();

await resumed.ExecuteAsync(ct);
```

**`FileDebateStore`** использует атомарную запись JSON с rename, так что
крэш в середине записи не оставит повреждённый чекпоинт.
`InMemoryDebateStore` — для тестов. Каждый чекпоинт содержит снимок
`DebateCheckpoint` (DebateId, CreatedAt, LastCompletedRound, CompletedRounds,
Options snapshot, OriginalQuestion). DebateId — ULID-style 26-символьные
лексикографически сортируемые идентификаторы.

Настройка ретенции через секцию `appsettings.json` `Delibera:Persistence`
(Enabled, Store, Directory, RetentionDays).

---

## F-04 — Память агентов

Участники совета подгружают контекст из предыдущих сессий и сохраняют
свои выводы после каждого совета.

```csharp
var memory = new InMemoryAgentMemory();
// Или: new QdrantAgentMemory(ragProvider, embeddingProvider);
// Или: new PgVectorAgentMemory(ragProvider, embeddingProvider);

var executor = new CouncilBuilder()
    .AddMember("llama3.2", llm, "Architect")
    .AddMember("qwen2.5", llm, "Pragmatist")
    .WithStandardDebate()
    .WithUserPrompt("Should we adopt event-driven architecture?")
    .WithMaxRounds(2)
    .WithAgentMemory(memory)  // ← F-04
    .Build();

await executor.ExecuteAsync();  // участники подгружают из предыдущих сессий
await executor.ExecuteAsync();  // и сохраняют новые выводы для следующего раза
```

**Перед исполнением** исполнитель подгружает top-3 воспоминания каждого
участника (дедупликация по содержимому) и добавляет блок `Memory from
previous sessions` к system-промпту. **После исполнения** он сохраняет
последний ответ каждого участника и вердикт Chairman как `MemoryEntry`,
помеченный именем участника и (если включена персистентность) debate id.

`InMemoryAgentMemory` использует схожесть Жаккара по токенам. Qdrant и
PgVector бэкенды используют существующий `IRagProvider` для хранения и
`IEmbeddingProvider` для семантической схожести — изолируя по агенту
через отдельные коллекции (Qdrant) или фильтрацию по metadata (pgvector).

---

## Демо-записи ConsoleApp

Каждая новая фича имеет `--flag` запись в демо-меню `Delibera.ConsoleApp`
(обнаруживается через `dotnet run` без аргументов):

| Флаг | Пример | Описание |
| --- | --- | --- |
| `--telemetry` | `TelemetryExample` | In-process `ActivityListener` + `MeterListener`, печатает каждый спан и метрику. |
| `--quick-wins` | `QuickWinsExample` | HTML-экспорт, таймаут, персоны, бенчмарк, лимит участников. |
| `--templates` | `TemplatesExample` | `DebateTemplate.ArchitectureReview` против живого Ollama. |
| `--stream` | `StreamingCouncilExample` | `IAsyncEnumerable<DebateRound>` live output. |
| `--adaptive-strategy` | `AdaptiveStrategyExample` | `StandardDebate` → `CritiqueDebate` переключение при стагнации. |
| `--voting` | `VotingExample` | `WeightedVotingStrategy` с весами по участникам. |
| `--structured-output` | `StructuredOutputExample` | `ArchitectureDecision` типизированный вердикт. |
| `--persistence` | `PersistenceExample` | `FileDebateStore` с ретенцией + auto-resume на существующий чекпоинт. |
| `--agent-memory` | `AgentMemoryExample` | `InMemoryAgentMemory` между последовательными дебатами. |

---

## Совместимость

- **Без breaking changes.** Каждая новая фича opt-in через дополнительные
  методы билдера (`WithTelemetry`, `WithTimeout`, `WithVotingChairman`,
  `WithStructuredOutput<T>`, `WithPersistence`, `WithAgentMemory`,
  `WithAdaptiveStrategy`). Fluent API полностью обратно совместим.
- Существующие реализации `ICouncilExecutor` продолжают работать — новые
  `StreamDebateAsync` и `ExecuteTypedAsync<TVerdict>` — это DIM с дефолтными
  реализациями, композирующими с существующим `ExecuteAsync`.

## Итоги тестирования

- 9 новых тест-файлов, 163 новых теста, **все 259 тестов проходят** (со 105 в
  v10.2.5).
- Покрытие включает: roundtrip сериализатора, генерацию схемы, отмену
  стрима, детекцию стагнации, математику голосования, логику retry
  структурированного вывода, сохранение/восстановление чекпоинта,
  изоляцию памяти по агенту и сквозные пути исполнения.

## Гайд по миграции

При обновлении с v10.2.5 до v10.2.6:

1. **Поднимите версию пакета** в `csproj` до `10.2.6`.
2. **Изменения кода не требуются.** Все новые фичи opt-in.
3. **Чтобы включить любую новую фичу**, вызовите соответствующий метод
   `With*` на `CouncilBuilder` — например, `.WithTelemetry()`,
   `.WithTimeout(...)`, `.WithVotingChairman(...)` и т.д.
4. **Чтобы использовать новые поверхности** (например, `StreamDebateAsync`,
   `AgentMemory`), приведите ваш executor к `CouncilExecutor` или
   используйте интерфейс `ICouncilExecutor` — оба предоставляют новые
   свойства.

## См. также

- [CHANGELOG.md](../CHANGELOG.md) — полная история версий
- [docs/v10.2.6.md](v10.2.6.md) — оригинальный технический roadmap
- [docs/QuickStart.md](QuickStart.md) / [docs/QuickStart-RU.md](QuickStart-RU.md) — быстрый старт
- [docs/NET10-Upgrade.md](NET10-Upgrade.md) — заметки по обновлению на .NET 10
- [docs/ChatClientLLMProvider.md](ChatClientLLMProvider.md) — мост Microsoft.Extensions.AI
