# Delibera v10.3.0 — Что нового

> **Статус:** ✅ Выпущен — Июль 2026
> **337 модульных тестов пройдено.**
> **Нарушающие изменения** — см. руководство по миграции ниже.

Этот документ описывает три крупных функции и нарушающие изменения в Delibera v10.3.0.
Полный список изменений см. в [CHANGELOG.md](../CHANGELOG.md). Дорожную карту см. в [docs/ROADMAP.md](ROADMAP.md).

---

## Содержание

- [P-01 — Нарушающие изменения](#p-01--нарушающие-изменения)
- [S-01 — Распределённые дебаты](#s-01--распределённые-дебаты)
- [S-03 — Кэширование результатов](#s-03--кэширование-результатов)
- [Delibera.Server — ASP.NET Core сервис](#deliberaserver--aspnet-core-сервис)
- [Очистка перед релизом](#очистка-перед-релизом)

---

## P-01 — Нарушающие изменения

v10.3.0 удаляет устаревшие API, помеченные `[Obsolete]` начиная с v10.1. При обновлении с v10.2.x внесите следующие изменения:

| Удалено | Замена |
|---------|--------|
| Статический класс `Moderator` | `Chairman` |
| `ICouncilBuilder.SetModerator(CouncilMember)` | `ICouncilBuilder.SetChairman(CouncilMember)` |
| `ICouncilBuilder.SetModerator(string, ILLMProvider, string?)` | `ICouncilBuilder.SetChairman(string, ILLMProvider, string?)` |
| Интерфейс `IDebateStrategyWithOptions` | `IDebateStrategy` (полная сигнатура с `DebateExecutionOptions`) |
| Перегрузка `IDebateStrategy.ExecuteAsync` без `DebateExecutionOptions` | Используйте перегрузку с `DebateExecutionOptions` (передайте `DebateExecutionOptions.Default`) |
| Возврат `null` по умолчанию в `ILLMProvider.GetModelCapabilitiesAsync` | Реализуйте явно; возвращайте `ModelCapabilities.Unknown(model)` при неизвестности |
| Возвращаемый тип `ModelCapabilities?` (nullable) | `ModelCapabilities` (non-nullable); проверяйте `caps.IsUnknown` вместо `caps is null` |
| Класс `RagProviderFactory` | `VectorStoreFactory` |
| Интерфейс `IRagProviderFactory` | `IVectorStoreFactory` |
| Значение enum `DebateStatus.Paused` | Удалено — никогда не присваивалось |
| Значение enum `DebateOrchestrationStatus.Pending` | Удалено — никогда не присваивалось |

### Исправление `WeightedVotingStrategy.ResolveWeight`

`ResolveWeight` теперь использует `ballot.Weight` в качестве отката вместо `defaultWeight` конструктора. Пошаговые веса работают как описано в документации.

---

## S-01 — Распределённые дебаты

Delibera теперь поддерживает распределённую оркестрацию дебатов. Абстракция `IDebateOrchestrator` отделяет выполнение дебатов от транспортного уровня, обеспечивая как локальные, так и Redis-развертывания.

### Основной интерфейс

```csharp
public interface IDebateOrchestrator
{
    Task<DebateResult> ExecuteAsync(ICouncilBuilder builder, CancellationToken ct = default);
    Task<DebateHandle> EnqueueAsync(string debateId, ICouncilBuilder builder, CancellationToken ct = default);
    Task<DebateHandle?> GetStatusAsync(string debateId, CancellationToken ct = default);
    IAsyncEnumerable<DebateRoundEvent> StreamAsync(string debateId, CancellationToken ct = default);
    Task<bool> CancelAsync(string debateId, CancellationToken ct = default);
}
```

### Локальный режим (по умолчанию)

```csharp
services.AddDelibera(configuration);
// LocalDebateOrchestrator регистрируется по умолчанию
```

### Режим Redis

```csharp
services.AddDelibera(configuration);
services.AddRedisDebateOrchestrator(configuration);
```

События раундов публикуются в Redis Streams, поэтому любой подключённый API-сервер может транслировать их через SSE.

Полная архитектура и конфигурация описаны в [docs/distributed-debates.md](distributed-debates.md).

---

## S-03 — Кэширование результатов

Избегайте повторного выполнения идентичных дебатов. Ключ кэша — SHA-256 хеш вопроса, участников, стратегии и конфигурации.

### Режимы кэширования

| Режим | Чтение | Запись | Сценарий использования |
|-------|--------|--------|-------------------------|
| `Disabled` | Нет | Нет | По умолчанию — без кэширования |
| `ReadWrite` | Да | Да | Продакшн — вернуть из кэша или выполнить и закэшировать |
| `ReadOnly` | Да | Нет | Устаревший кэш допустим, новые результаты не кэшируются |
| `WriteThrough` | Нет | Да | Всегда выполнять, всегда кэшировать (прогрев) |
| `Bypass` | Нет | Нет | Принудительное выполнение даже при настроенном кэше |

### Быстрый старт

```csharp
// В памяти (разработка)
services.AddDelibera(configuration)
    .UseInMemoryCache(ttl: TimeSpan.FromHours(1));

// Файловый кэш
services.AddDelibera(configuration)
    .UseFileCache(directory: "./debate-cache", ttl: TimeSpan.FromDays(7));

// Redis (продакшн)
services.AddRedisDebateOrchestrator(configuration);
services.UseRedisCache(ttl: TimeSpan.FromHours(24));
```

### Управление кэшем на уровне дебата

```csharp
var executor = new CouncilBuilder()
    .WithUserPrompt("Какая архитектура лучше?")
    .AddMember("gpt-4o", provider, "Архитектор")
    .WithCacheBehavior(CacheBehavior.ReadWrite)
    .Build();

var result = await executor.ExecuteAsync();
Console.WriteLine($"Кэш-попадание: {result.CacheHit}");  // true если из кэша
Console.WriteLine($"Ключ кэша: {result.CacheKey}");     // напр. "A3F2B8C1D4E5F6A7"
Console.WriteLine($"Время кэширования: {result.CachedAt}"); // оригинальная метка времени
```

Полная справка — в [docs/caching.md](caching.md).

---

## Delibera.Server — ASP.NET Core сервис

Новый проект `Delibera.Server` предоставляет самохостящийся ASP.NET Core 10 Minimal API:

- **REST API**: `POST /api/v1/debates` (синхронно), `POST /api/v1/debates/async` (фоново), `GET /api/v1/debates/{id}`, `DELETE /api/v1/debates/{id}`
- **SSE-стриминг**: `GET /api/v1/debates/{id}/stream` — на базе `IDebateOrchestrator.StreamAsync()`
- **Сценарии**: `POST /api/v1/scenarios` для произвольных дебатов
- **OpenAPI**: Swagger UI на `/openapi`

Полная справка API — в [docs/Server.md](Server.md).

---

## Очистка перед релизом

- Удалены `DebateStatus.Paused` и `DebateOrchestrationStatus.Pending` — неиспользуемые значения enum
- Исправлено предупреждение CS8618 для `DebateRecord.Label` (теперь по умолчанию `string.Empty`)
- `DebateResponse` теперь включает поля `Label`, `CacheHit` и `CacheKey`
- `SseDebateStreamWriter` переписан для использования `IDebateOrchestrator.StreamAsync()` вместо `DebateRecord.RoundReader`
- `DebateOrchestrationService` переписан с опроса каждые 200мс на событийно-ориентированный `StreamAsync()`
- Все предупреждения CS1591/CS1574 XML-doc исправлены в `Delibera.Core`