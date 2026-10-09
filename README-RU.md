<div align="center">

<img src="img/delibera-horizontal-1920x480.png" alt="Delibera" width="640">

# Delibera

### ⚖️ Продуманные решения с помощью ИИ

**Коллективное принятие решений через структурированное обсуждение ИИ — с RAG, pgvector, Knowledge Keeper, 🛠️ Operator (MCP-инструменты), Chairman, 🔥 сжатием контекста, ✂️ AutoChunking, 💉 Dependency Injection и 📋 журналированием выполнения**

[![NuGet](https://img.shields.io/nuget/v/Delibera.Core.svg)](https://www.nuget.org/packages/Delibera.Core)
[![NuGet: Server](https://img.shields.io/nuget/v/Delibera.Server.svg)](https://www.nuget.org/packages/Delibera.Server)
[![NuGet: Redis](https://img.shields.io/nuget/v/Delibera.Redis.svg)](https://www.nuget.org/packages/Delibera.Redis)
[![Release](https://img.shields.io/github/v/release/techbuzzz/Delibera?label=Release)](https://github.com/techbuzzz/Delibera/releases/latest)
[![Docker Hub](https://img.shields.io/badge/Docker%20Hub-2496ED?logo=docker&logoColor=fff)](https://hub.docker.com/r/techbuzzz/delibera-server)
[![Pulls: server](https://img.shields.io/docker/pulls/techbuzzz/delibera-server?logo=docker&logoColor=2496ED)](https://hub.docker.com/r/techbuzzz/delibera-server)
[![Pulls: webui](https://img.shields.io/docker/pulls/techbuzzz/delibera-webui?logo=docker&logoColor=2496ED)](https://hub.docker.com/r/techbuzzz/delibera-webui)
[![Web UI](https://img.shields.io/badge/Web%20UI-GitHub%20Pages-3E8AFF?logo=githubpages)](https://techbuzzz.github.io/Delibera/)
[![CI](https://github.com/techbuzzz/Delibera/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/techbuzzz/Delibera/actions/workflows/publish-nuget.yml)
[![Docker CI](https://github.com/techbuzzz/Delibera/actions/workflows/publish-docker.yml/badge.svg)](https://github.com/techbuzzz/Delibera/actions/workflows/publish-docker.yml)
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
| **🗜️ Сжатие контекста**          | 4 стратегии (Semantic, Deduplication, Summarization, Hybrid) применяются к промпту каждого раунда — [измерено 11.7–12.1% prompt-токенов](docs/performance-measurements.md#3-context-compression) |
| **✂️ AutoChunking**              | Прогрессивное раскрытие больших документов по раундам — с учётом контекстных окон моделей |
| **🌐 Распределённые дебаты**      | `IDebateOrchestrator` с локальным и Redis-бэкендами — параллелизация дебатов между машинами |
| **💾 Кэширование результатов**    | `IDebateCache` с in-memory, файловым и Redis-бэкендами — пропуск повторных идентичных дебатов |
| **🖥️ Delibera.Server**           | ASP.NET Core 10 Minimal API с REST + SSE-стримингом для дебатов по HTTP              |
| **🔧 Tool Use**                  | `IToolProvider` + `AIFunction` — участники вызывают инструменты прямо в дебате. Нативный вызов функций там, где провайдер его поддерживает, иначе маркеры `[[TOOL: …]]`. Готовые провайдеры: файловая система, HTTP и MCP |
| **💸 Потолки бюджета и rate limits** | Жёсткие ограничения расходов и лимиты вызовов по моделям. При превышении возвращается деградированный результат с потраченным, а не исключение |
| **🔀 Diff дебатов**              | `baseline.Diff(candidate)` — пословное сравнение двух прогонов в Markdown/HTML, с сопоставлением по номеру раунда и имени участника |
| **⌨️ CLI delibera**              | `run`, `resume`, `compare`, `benchmark` прямо из терминала |
| **📡 gRPC-транспорт**            | События дебата серверным стримом по gRPC, поверх `IDebateOrchestrator`, поэтому кэширование и распределённое исполнение ведут себя так же |
| **💉 Dependency Injection**       | Расширение `AddDelibera()` для `IServiceCollection` с полной привязкой опций         |
| **📋 Журналирование выполнения**  | Модель `ExecutionLog` с `LogLevel` — события Chairman, KK, сжатия и участников        |
| **📁 Раздельный вывод файлов**    | Экспорт `result.md`, `statistics.md` и `logs.md` по отдельности                     |
| **🔌 Interface-First**            | Чистые абстракции для провайдеров, фабрик, билдеров и исполнителей                   |
| **🤝 Microsoft.Extensions.AI**     | Поддержка `IChatClient` / `IEmbeddingGenerator` — подключайте OpenAI, Azure OpenAI, Ollama и любые совместимые бэкенды, с middleware (function calling, логирование) |
| **🧱 Современный C# 15 (preview)** | Построено на .NET 10 с `LangVersion=preview`, file-scoped namespaces, records, span/SIMD горячие пути |

### 🆕 Что нового в v10.5.2

Браузерный интерфейс для работы с дебатами, реально подключённый Redis внутри сервера, образы в
Docker Hub и четыре исправления Web UI — одно из них делало невозможным создание дебаты из браузера.

**Содержит два ломающих изменения поведения:** все публикуемые порты теперь слушают `127.0.0.1`
вместо `0.0.0.0`, а `Delibera.Server` получает транзитивную зависимость на `Delibera.Redis`.
Подробности: [docs/WhatsNew-v10.5.2.md](docs/WhatsNew-v10.5.2.md).

| Возможность | Описание |
| --- | --- |
| **🖥️ Веб-интерфейс** | `src/Delibera.WebUI` — Nuxt 4, поставляется вторым контейнером. Список, создание, живые SSE-раунды, вердикт со статистикой токенов, экспорт в Markdown, отмена. Ходит в API **только** через Nitro-прокси: сервер не регистрирует CORS-политику, а `DebateMapper` строит абсолютные URL без учёта forwarded-заголовков. SSE проксируется без буферизации, что проверено на реальном сокете. |
| **🔌 Redis в сервере** | `Delibera:Redis:Enabled` регистрирует Redis-оркестратор и, опционально, Redis-кэш результатов. По умолчанию выключено. Раньше `Delibera.Server` ссылался только на `Delibera.Core`, поэтому сервис `redis` в compose подключался в пустоту. **Не** даёт распределённого исполнения раундов — `DebateWorkerService` намеренно заглушка. |
| **🐳 Docker Hub** | `techbuzzz/delibera-server` и `techbuzzz/delibera-webui`, публикуются из тегов `v*` как `linux/amd64` + `linux/arm64`. Ollama не перепубликовывается. |
| **🔒 Только localhost** | Все публикуемые порты слушают `127.0.0.1`. API не аутентифицирован и не ограничен по частоте, а дебата тратит реальные кредиты — привязка это и есть enforcement, а не украшение. |

Тесты: **620 проходят** (493 Core + 117 Server + 10 gRPC), 0 упавших, 0 пропущенных, плюс 34 теста
Web UI. Релизная сборка чистая под `-warnaserror`.

### 🆕 Что нового в v10.5.1

Закрыты все шесть открытых issue и добавлен фикс, который мог проявиться только на живой базе.
Breaking changes нет — всё опционально, ни один публичный интерфейс не получил новых членов. Подробности:
[docs/WhatsNew-v10.5.1.md](docs/WhatsNew-v10.5.1.md).

| Возможность | Описание |
| --- | --- |
| **🔧 Tool Use** | `IToolProvider` + `AIFunction`. `CouncilBuilder.WithTools(...)`. Нативный вызов функций там, где провайдер оборачивает настоящий `IChatClient`; в остальных случаях — протокол маркера `[[TOOL: name {json}]]`, потому что строковый адаптер физически не может нести структурированный tool-трафик. Цикл принадлежит `FunctionInvokingChatClient`, ограничен `WithMaxToolIterations(n)`. В комплекте `FileSystemToolProvider` (с корнем, отсекающий traversal), `HttpToolProvider` (allow-list хостов, отказывает в plain HTTP), `McpToolProvider`. Вызовы попадают в `DebateRound.ToolCalls` / `DebateResult.ToolCalls`. |
| **💸 Потолки бюджета и rate limits** | `WithCostLimit(decimal, CostLimitBehavior)` и `WithTokenBudget(long)`; `WithRateLimit(n, window)`. `DebateResult.CostEstimate` показывает расход по участникам с флагом `IsEstimate`. Превышение возвращает **деградированный результат с потраченным**, а не исключение: потолок, который бросает, оставляет вас без сведений о том, сколько уже потрачено. `Build()` бросает, если денежный потолок задан без прайс-листа, — такая комбинация не может сработать никогда. |
| **🔀 Diff дебатов** | `baseline.Diff(candidate)` → `DebateDiff` с пословным `**добавлено**` / `~~удалено~~`, схожестью вердиктов и явными списками раундов или участников, присутствующих только с одной стороны. `ToMarkdown()`, `ToHtml()`, `SaveToHtmlAsync(path)`. Раунды сопоставляются по номеру, участники — по имени, поэтому прогон на 4 раунда против прогона на 3 раунда покажет недостающий раунд, а не сдвинет все последующие сравнения. |
| **⌨️ CLI delibera** | `delibera run \| resume \| compare \| benchmark` на System.CommandLine. `compare` сравнивает два сохранённых результата; `benchmark` показывает разброс по прогонам, а не одно число. |
| **📡 gRPC** | `Delibera.Grpc` + `Delibera.Grpc.Client`, построены **поверх** `IDebateOrchestrator`, поэтому кэширование и распределённое исполнение ведут себя так же. Серверный стрим всегда завершается ровно одним терминальным событием. |
| **📦 NuGet GA** | `Delibera.Core`, `Delibera.Server` и `Delibera.Redis` теперь все публикуемы. Workflow падает, если пакет уходит без README или иконки. |

Два дефекта, исправленных здесь, были не видны юнит-тестам, потому что тесты работали через фейки:

| Фикс | Описание |
| --- | --- |
| **🐘 pgvector вообще не умел ни писать, ни искать** | `AddWithValue(new Vector(...))` боксит значение, поэтому Npgsql не мог вывести тип, и **первый же upsert падал** с `InvalidCastException`. Объявление `NpgsqlDbType.Unknown` тоже не помогает: `UseVector()` регистрирует маппинг только под ту версию Npgsql, под которую собран `Pgvector`. Вектор теперь передаётся текстовым литералом pgvector с кастом `::vector` — понимает любая версия. Qdrant не затронут. |
| **🪞 Идемпотентная индексация** | `BaseRagProvider` выдавал `Guid.NewGuid()` на каждый чанк, а оба стора используют ID как ключ upsert — переиндексация дописывала. Три прогона по 24 чанкам давали **72 точки**. Теперь ID выводится из идентичности чанка. Проверено на живых Qdrant и pgvector: три прогона документа из 6 чанков дают 6. |

### 🆕 Что нового в v10.5.0

Пять дефектов, найденных при первом реальном замере дебатов против Ollama Cloud. Два из них делали
заявленные возможности молча нерабочими — полные доказательства в
[docs/performance-measurements.md](docs/performance-measurements.md).

| Фикс | Описание |
| --- | --- |
| **🗜️ Сжатие наконец работает** | `CompressTextAsync` был публичным API, который никто в пайплайне не вызывал; `TokenStats` оставался null, а десять замеров со сжатием давали **0.00% экономии при 0 мс накладных**. Теперь компрессор применяется к промпту каждого раунда, каждый проход логируется. Замерено **11.7–12.1%** — всё ещё сильно меньше прежних заявленных 30–70%, и документация это теперь отражает. |
| **🌐 Ollama Cloud вообще работал** | Провайдер по умолчанию слал `num_predict: -1`, который эндпоинт отвергает: `max_tokens must be positive`. **Каждый запрос падал до генерации первого токена.** Неположительный лимит теперь просто не отправляется. |
| **⚠️ Деградация стала видна** | Упавший участник подставлялся в транскрипт как `"[ERROR: ...]"` и читался Chairman'ом как мнение. Теперь он исключается из раунда и попадает в `DebateResult.FailedMembers` + `IsDegraded`. |
| **🔍 Пустой ответ объяснён** | Новый `OllamaEmptyResponseException` отличает «бюджет ушёл в размышления» от «модель не вернула ничего» и прямо пишет, что делать. |
| **🧠 Размышления под контролем** | `OllamaProvider(enableThinking:)` отправляет `Think` явно; `retryOnBudgetExhaustion` делает один повтор с увеличенным бюджетом, когда генерацию обрезали на середине мысли. |
| **⏱️ Длительности были отрицательными** | `TotalDuration` давал `-0.0s` на каждом дебате: `StartedAt` инициализировался уже после простановки `CompletedAt`. |

### 🆕 Что нового в v10.3.0

| Возможность | Описание |
| --- | --- |
| **🌐 Распределённые дебаты** | Интерфейс `IDebateOrchestrator` с `LocalDebateOrchestrator` (один процесс) и `RedisDebateOrchestrator` (Redis pub/sub для диспетчеризации раундов и сбора результатов). `DebateHandle`, `DebateOrchestrationStatus`, `DebateRoundEvent`, `DebateWorkerService`, `RedisOrchestratorOptions`, `RedisOrchestratorExtensions`. Подключение через `ICouncilBuilder.WithOrchestrator(IDebateOrchestrator)`. |
| **💾 Кэширование результатов** | Интерфейс `IDebateCache` с реализациями `InMemoryDebateCache`, `FileDebateCache`, `RedisDebateCache`. Перечисление `CacheBehavior` (`UseCache`, `BypassCache`, `RefreshCache`). `DebateCacheKeyGenerator` для детерминированных ключей кэша. Метаданные кэша на `DebateResult` (`CacheHit`, `CacheKey`, `CachedAt`). `ICouncilBuilder.WithCacheBehavior()` / `WithCache()`. Расширения DI + OTEL-счётчик `delibera.cache.hits`/`misses`. |
| **🖥️ Delibera.Server** | ASP.NET Core 10 Minimal API для запуска Delibera по HTTP. REST API (`POST /api/debates`, `GET /api/debates/{id}`, `DELETE /api/debates/{id}`), SSE-стриминг через `GET /api/debates/{id}/stream` на базе `IDebateOrchestrator.StreamAsync()`, фоновая очередь через `IHostedService` + `System.Threading.Channels`. |
| **⚠️ Критические изменения** | `Moderator` → `Chairman`; удалён `IDebateStrategyWithOptions`; `ModelCapabilities` стал ненулевым (используйте `IsUnknown`); `RagProviderFactory` → `VectorStoreFactory`; удалены `DebateStatus.Paused` и `DebateOrchestrationStatus.Pending`; `SseDebateStreamWriter` переписан для `IDebateOrchestrator.StreamAsync()`; удалены `DebateRecord._channel`/`RoundWriter`/`RoundReader`. |

> Полные release notes смотрите в [CHANGELOG.md](CHANGELOG.md), что именно замеряли —
> в [docs/performance-measurements.md](docs/performance-measurements.md), дорожную карту — в
> [docs/ROADMAP.md](docs/ROADMAP.md).

---

## 💡 Сценарии применения

Одна и та же форма совета подходит ко многому. Вот сценарии, где несколько моделей, спорящих
друг с другом, действительно дают больше, чем один ответ от одной модели.

| Сценарий | Форма совета | Почему это стоит денег |
|---|---|---|
| **Архитектурное решение** | 3 эксперта + chairman, 4 раунда, RAG по вашим ADR | Разные модели подсвечивают разные классы отказов, а Chairman обязан назвать, где именно они разошлись. Замерено: архитектурный вердикт поднял два риска, о которых не сказал никто, — смену лицензии и незаданный вопрос *зачем* вообще нужна миграция. |
| **Код-ревью перед человеком** | Кодовый специалист + 2 универсала, 3 раунда | Ловит то, что линтер пропускает: неверное исправление и невысказанное допущение. Замерено: самый дешёвый ростер на вопрос «назови один дефект» потратил 78 000 символов и всё равно выбрал более слабый из двух доступных ответов. |
| **Разбор инцидента** | 3 эксперта, chairman, RAG по runbook'ам + `chrome-devtools-mcp` за живыми доказательствами | Разделяет в протоколе «что мы знаем» и «что мы предполагаем». |
| **Security и комплаенс-ревью** | 3 эксперта, chairman, с проверкой `IsDegraded` | Упавший участник — это дыра в ревью, а не сноска: `IsDegraded` делает её невозможно пропустить. |
| **Выбор вендора или инструмента** | 3 эксперта + chairman, критерии в промпте | Превращает спор о вкусах в явную таблицу компромиссов с сохранённым dissent'ом. |
| **Исследование с цитатами** | Knowledge Keeper по вашим докам, 4 раунда | Замерено: выдача вернула нужный фрагмент (top score 0.738), а вердикты ссылались на документы по диапазонам строк. |
| **Поддержка on-call** | 2–3 эксперта, 2 раунда, с кэшем | Один и тот же инцидент второй раз стоит одного дебата: замерено попадание в кэш за **0.0 с** против 154–209 с без кэша. |
| **Ревью промптов и агентов** | 3 эксперта, chairman | Находит промпт, где побеждает самая уверенная модель, а не самая рассуждающая. |

**Когда *не* надо:** разовые фактические вопросы, любые задачи, где один сильная модель с вызовом
инструмента лучше трёх мнений, и бюджеты меньше секунды. Замеренный дебат в 4 раунда идёт
**154–209 с**.

---

## 📑 Содержание

- [Сценарии применения](#-сценарии-применения)
- [Быстрый старт](#-быстрый-старт)
  - [Требования и модели](#требования-и-модели)
  - [Установка](#установка)
  - [Минимальный пример](#минимальный-пример)
  - [Запуск](#запуск)
- [Dependency Injection](#-dependency-injection)
- [Operator (MCP-инструменты)](#️-operator-mcp-инструменты)
- [Сжатие контекста](#️-сжатие-контекста)
- [AutoChunking](#-autochunking)
- [Интеграция RAG](#-интеграция-rag)
- [Стратегии дебатов](#️-стратегии-дебатов)
- [Структура выходных файлов](#-структура-выходных-файлов)
- [Измеренное поведение](#-измеренное-поведение)
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

Delibera публикуется как три пакета NuGet и два образа Docker. Выберите подходящий путь.

#### 📦 NuGet

```bash
dotnet add package Delibera.Core      # сам фреймворк — начните отсюда
dotnet add package Delibera.Server    # хост ASP.NET Core (Minimal API, SSE, MCP)
dotnet add package Delibera.Redis     # распределённые дебаты + общий кэш результатов
```

Все три выходят в каждом релизе с одной версией. `Delibera.Server` подтягивает `Delibera.Redis`
транзитивно, поэтому явная ссылка нужна только если вы используете Redis-интеграцию напрямую.
Redis остаётся **выключенным**, пока не задан `Delibera:Redis:Enabled`.

#### 🐳 Docker Hub — без сборки

Образы публикуются на каждом теге `v*` для `linux/amd64` **и** `linux/arm64`:

| Образ | Что это |
| --- | --- |
| `techbuzzz/delibera-server` | API, SSE-стриминг дебат, MCP-эндпоинты |
| `techbuzzz/delibera-webui` | браузерный интерфейс на Nuxt 4 |

```bash
curl -O https://raw.githubusercontent.com/techbuzzz/Delibera/main/deploy/docker-compose.hub.yml
export DELIBERA_VERSION=10.5.2      # зафиксировать релиз; без переменной тянется :latest
docker compose -f docker-compose.hub.yml up -d
```

> ⚠️ **API не аутентифицирован и не ограничен по частоте, а дебата тратит реальные кредиты LLM.**
> Все публикуемые порты слушают `127.0.0.1`. Поставьте аутентифицирующий обратный прокси, прежде чем
> открывать этот стек в сеть, которой не управляете.

#### 📥 Из исходников

```bash
git clone https://github.com/techbuzzz/Delibera.git
cd Delibera
dotnet build Delibera.slnx -c Release
```

Из клона `docker compose up -d` поднимает весь стек — сервер, веб-интерфейс, Ollama, Qdrant,
pgvector и Redis. Пошаговое руководство: [docs/QuickStart-RU.md](docs/QuickStart-RU.md).

> 🧑‍🔧 **Публикуете релиз или обновляетесь со старой версии?** Смотрите
> [docs/ReleaseProcess.md](docs/ReleaseProcess.md) — там описан порядок, который важен, и два
> шага, которые падают молча, а не с ошибкой.

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
| `IVectorStoreFactory` | `VectorStoreFactory` | Singleton   |
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

Автоматически сжимайте контекст между раундами обсуждения — по замерам **11.7–12.1% токенов** без потери смысла.

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

## ✂️ AutoChunking

Автоматическое разбиение больших документов (договоры, отчёты, статьи) на чанки,
соответствующие размеру контекстного окна моделей, с распределением по раундам дебатов
через **прогрессивное раскрытие** (progressive disclosure). Если документ превышает
контекстное окно самой маленькой модели, оркестратор создаёт план чанкинга и равномерно
распределяет чанки — каждая модель получает полное представление к финальному раунду.

### Как это работает

1. **Определение моделей** — запрос контекстного окна каждой модели через
   `GetModelCapabilitiesAsync()` (Ollama `/api/show` или встроенный реестр из 40+ моделей).
2. **Расчёт накладных расходов** — системный промпт + вопрос + буфер ответа + история.
3. **План чанкинга** — разбивка документа по семантическим границам (Markdown-заголовки →
   параграфы → предложения), каждый чанк ≤ `minWindow − overhead − safetyMargin`.
4. **Прогрессивное раскрытие** — Раунд 1 получает чанки 1..N/3, Раунд 2 — N/3+1..2N/3 и т.д.
   Каждый раунд видит маркеры `[Chunk X/Y] SectionTitle` + сводку предыдущих раундов.

### Три способа конфигурации

```csharp
// Способ 1: Fluent API
var executor = new CouncilBuilder()
    .WithAutoChunking(new AutoChunkingOptions
    {
        Strategy = ChunkingStrategy.SemanticBoundary,
        SafetyMargin = 0.15,
        MaxChunksPerRound = 3
    })
    /* …участники, chairman, знания… */
    .Build();

// Способ 2: Объект CouncilOptions (из DI или вручную)
var options = new CouncilOptions
{
    AutoChunking = new AutoChunkingConfig { Enabled = true, Strategy = "SemanticBoundary" }
};
var executor = new CouncilBuilder(options)  // или .WithOptions(options)
    /* … */
    .Build();

// Способ 3: Лямбда-конфигурация
var executor = new CouncilBuilder()
    .WithOptions(o =>
    {
        o.AutoChunking.Enabled = true;
        o.AutoChunking.MaxChunksPerRound = 2;
    })
    /* … */
    .Build();
```

### Конфигурация (`appsettings.json`)

```json
{
  "Delibera": {
    "AutoChunking": {
      "Enabled": true,
      "Strategy": "SemanticBoundary",
      "SafetyMargin": 0.15,
      "MaxChunksPerRound": 3,
      "EnableMapReduce": true,
      "EnableProgressiveDisclosure": true,
      "ModelContextWindows": {
        "my-custom-model": 65536
      }
    }
  }
}
```

### Реестр контекстных окон моделей

```csharp
// Запрос известных моделей
var window = ModelContextWindowRegistry.GetContextWindow("llama3.2"); // → 131072
var window = ModelContextWindowRegistry.GetContextWindow("phi3:mini"); // → 4096

// Регистрация своих моделей
ModelContextWindowRegistry.Register("my-fine-tuned-model", 65536);
```

> ▶️ Запустите демо: `dotnet run --project src/Delibera.ConsoleApp -- --autochunking`

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
var ragFactory = new VectorStoreFactory();
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
| **Factory**         | `ProviderFactory`, `VectorStoreFactory`, `CompressionFactory`, `Chairman` |
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

## 📏 Измеренное поведение

Цифры ниже — из реальных дебатов против Ollama Cloud, а не оценки. Методика, оговорки и все
доказательства — в [docs/performance-measurements.md](docs/performance-measurements.md).

| Что | Замерено |
|---|---|
| **Накладные расходы фреймворка** | **0.0 с** — время, которое не объясняется ни одним вызовом модели |
| **Время дебата** | 154–209 с на 4 раунда, 3 участника + chairman |
| **Пропускная способность** | 840–1144 симв./с, стабильно — время идёт за объёмом текста, а не за Delibera |
| **Профиль стоимости раундов** | Критика и уточнение — **62–77%** дебата; первый раунд самый дешёвый |
| **Сжатие контекста** | **11.7–12.1%** prompt-токенов |
| **Knowledge Keeper** | 3 запроса на дебат, стабильно; top score пробного поиска 0.738 |
| **Operator (MCP)** | 1–3 делегированные задачи на дебат, растёт с темой |
| **Попадание в кэш** | **0.0 с** против 154–209 с без кэша |
| **Баланс вывода** | Одна многословная модель дала **68.8%** всего текста участников — состав ростера важнее фреймворка |

Две честные оговорки, прежде чем брать цифру:

- **Сжатие экономит ~12%, а не 30–70%, как было написано в этом README раньше.** Старая цифра
  никогда не измерялась. В v10.5.0 исправлено.
- **Дебат не быстрый.** 154–209 с — честная цена. Если это не влезает в бюджет, используйте два
  раунда, трёх участников или кэш.

---

## 💻 Примеры ConsoleApp

В репозитории есть [`Delibera.ConsoleApp`](src/Delibera.ConsoleApp/) — запускаемый демо-проект,
который задействует каждую возможность. Запускайте его из корня репозитория:

```bash
# Клонировать репозиторий
git clone https://github.com/techbuzzz/Delibera.git
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
dotnet run -- --autochunking       # 🆕 Демо AutoChunking (большие документы)

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
git clone https://github.com/techbuzzz/Delibera.git
cd Delibera

# Собрать всё решение
dotnet build --configuration Release

# Запустить консольное демо
cd src/Delibera.ConsoleApp
dotnet run
```

### Инфраструктура одной командой (Docker Compose)

Поднимите **API + веб-интерфейс + Redis + Qdrant + PostgreSQL/pgvector** за один шаг. (Ollama
намеренно не включена — [установите её нативно](https://ollama.com/download), чтобы GPU-драйвер
использовался напрямую.)

```bash
# Из корня репозитория
docker compose up -d

# Либо вместе с Ollama на GPU внутри Docker:
docker compose --profile ollama up -d
```

Затем откройте **http://localhost:3000** — веб-интерфейс, и http://localhost:5200 — API.

Compose-стек предоставляет:

| Сервис                | URL                        | Назначение                             |
| --------------------- | -------------------------- | -------------------------------------- |
| Веб-интерфейс (Nuxt 4)| `http://localhost:3000`    | Дебаты: создание, раунды вживую, вердикт |
| REST API              | `http://localhost:5200`    | REST-эндпоинты под `/api/v1`           |
| MCP                   | `http://localhost:5200/mcp`| Инструменты Model Context Protocol     |
| Qdrant (REST / gRPC)  | `localhost:6333` / `:6334` | Векторное хранилище (используется Delibera) |
| PostgreSQL            | `localhost:5432`           | RAG-хранилище pgvector                 |
| Redis                 | только внутри сети        | Состояние дебатов (по желанию)         |

Учётные данные по умолчанию: `postgres` / `postgres`, база `council_vectors`.

> **⚠️ Безопасность — прочитайте это перед изменением привязки портов.**
>
> API Delibera **не аутентифицирован и не ограничен по частоте запросов**. Любой, кто достигнет
> порта, может запускать дебаты, расходующие реальные кредиты LLM в вашем провайдере. Именно
> поэтому все публикуемые порты привязаны к `127.0.0.1`. Не меняйте привязку на `0.0.0.0`, пока
> не поставите перед стеком прокси с аутентификацией.

> Если эти сервисы уже запущены нативно, просто пропустите `docker compose` и направьте консольное
> приложение на них — `appsettings.json` по умолчанию использует `localhost`.

#### Готовые образы (без клонирования репозитория)

```bash
curl -O https://raw.githubusercontent.com/techbuzzz/Delibera/main/deploy/docker-compose.hub.yml
docker compose -f docker-compose.hub.yml up -d
```

Забирает `techbuzzz/delibera-server` и `techbuzzz/delibera-webui` из Docker Hub. Укажите
`DELIBERA_VERSION=10.5.2`, чтобы зафиксировать релиз.

### Веб-интерфейс

[`src/Delibera.WebUI`](src/Delibera.WebUI/) — приложение на Nuxt 4, покрывающее работу с дебатами:
список, создание, раунды в реальном времени через SSE, вердикт со статистикой токенов, экспорт в
Markdown, отмена запущенной дебаты.

Два момента, важных при доработке:

- **Браузер никогда не обращается к API напрямую.** Все запросы проходят через серверный прокси
  Nitro (`/api/delibera/**`), потому что на API нет CORS-политики и кросс-оригинальный запрос из
  браузера был бы заблокирован. SSE передаётся потоком, а не буферизуется — тесты проверяют, что
  первый чанк приходит до завершения upstream-потока.
- **`VerdictDto` — в основном пустая оболочка.** `DebateMapper` заполняет только `recommendation`
  и `rawJson`; `confidence`, `riskLevel`, `risks` и `conditions` не заполняются никогда. Реальные
  структурированные данные лежат внутри `verdict.rawJson` и зависят от шаблона.

Локальный запуск против API на хосте:

```bash
cd src/Delibera.WebUI
npm ci
npm run dev          # http://localhost:3000, проксирует на http://localhost:5200
```

### Redis (по желанию)

Установите `Delibera:Redis:Enabled=true` (или `DELIBERA_REDIS_ENABLED=true`), чтобы сервер
публиковал раунды в Redis Stream вместо хранения их в процессе. Это позволяет нескольким
инстансам API обслуживать одни и те же дебаты, а также включает Redis-кэш результатов
(`CacheEnabled`).

По умолчанию **выключено**. Это также **не распределённое выполнение раундов** —
`DebateWorkerService` является задокументированной заглушкой, поэтому каждая дебата по-прежнему
выполняется в том процессе, который её принял. См.
[`src/Delibera.Redis/README.md`](src/Delibera.Redis/README.md).

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
| `Microsoft.Extensions.AI`| Унифицированные AI-абстракции `IChatClient` / `IEmbeddingGenerator` и middleware |
| `Microsoft.Extensions.*` | Конфигурация, DI и Options       |
| `StackExchange.Redis`   | Redis-клиент для распределённых дебатов и кэширования |

---

## 🤝 Microsoft.Extensions.AI

Delibera интегрирована с [**Microsoft.Extensions.AI**](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
(v10.7.0) — стандартным слоем абстракций .NET для генеративного AI. Это позволяет запускать совет
с **любым** бэкендом, реализующим `IChatClient` (OpenAI, Azure OpenAI, Ollama, Anthropic,
LM Studio / LocalAI / vLLM, …) и собирать **конвейер middleware** (вызов функций/инструментов,
логирование, кэширование, телеметрия) — без написания отдельного провайдера под каждого поставщика.

### Что вы получаете

| Тип | Роль |
| ---- | ---- |
| `ChatClientLLMProvider` | Адаптирует любой `IChatClient` к `ILLMProvider` Delibera (со **стримингом** через `ChatStreamAsync`) |
| `EmbeddingGeneratorProvider` | Адаптирует любой `IEmbeddingGenerator<string, Embedding<float>>` к `IEmbeddingProvider` для RAG |
| `MicrosoftAIExtensions` | Мосты: `AsLLMProvider()`, `AsEmbeddingProvider()`, `AsChatClient()`, `WithMiddleware()` |
| `ProviderFactory.CreateFromChatClient(...)` | Создаёт кэшируемый провайдер прямо из `IChatClient` |
| `AddDeliberaChatClient(...)` / `AddDeliberaEmbeddingGenerator(...)` | Регистрация стандартных AI-сервисов в DI |

### Использование любого `IChatClient` как провайдера Delibera

```csharp
using Delibera.Core.Extensions;
using Microsoft.Extensions.AI;

// 1) Подключите свой IChatClient. Для OpenAI:
//    dotnet add package Microsoft.Extensions.AI.OpenAI
IChatClient client = new OpenAI.Chat.ChatClient("gpt-4o-mini", apiKey).AsIChatClient();

// 2) (опционально) middleware — вызов функций + логирование
client = client.WithMiddleware(enableFunctionInvocation: true, loggerFactory);

// 3) отдайте его Delibera
ILLMProvider provider = client.AsLLMProvider("OpenAI");

var executor = new CouncilBuilder()
   .AddMember("gpt-4o-mini", provider, "Архитектор")
   .AddMember("gpt-4o-mini", provider, "Скептик")
   .SetChairman(Chairman.CreateStandard("gpt-4o-mini", provider))
   .WithStandardDebate()
   .WithUserPrompt("Модульный монолит или микросервисы для команды из 5 человек?")
   .Build();
```

Ollama работает «из коробки», так как `OllamaApiClient` из OllamaSharp нативно реализует
`IChatClient` и `IEmbeddingGenerator`:

```csharp
using var ollama = new OllamaProvider("http://localhost:11434");
IChatClient chat = ollama.AsChatClient();                 // готов к middleware
ILLMProvider provider = chat.AsLLMProvider("Ollama");
IEmbeddingProvider embeddings = ollama.AsEmbeddingGenerator().AsEmbeddingProvider("nomic-embed-text");
```

### Стриминг

```csharp
await foreach (var chunk in provider.ChatStreamAsync("gpt-4o-mini", systemPrompt, userPrompt))
   Console.Write(chunk);
```

`ChatStreamAsync` — это additive-метод по умолчанию в `ILLMProvider`: провайдеры на базе
`IChatClient` стримят токен за токеном, а старые провайдеры прозрачно используют один вызов.

### Внедрение зависимостей (DI)

```csharp
services.AddDeliberaChatClient(
   sp => new OpenAI.Chat.ChatClient("gpt-4o-mini", apiKey)
            .AsIChatClient()
            .WithMiddleware(enableFunctionInvocation: true),
   providerName: "OpenAI");

services.AddDeliberaEmbeddingGenerator(
   sp => /* ваш IEmbeddingGenerator */,
   modelName: "text-embedding-3-small");
```

> Попробуйте: `dotnet run --project src/Delibera.ConsoleApp -- --msai`

---

## 🏛️ Архитектура

```
Delibera.Core
├── Council/              ← CouncilBuilder, CouncilExecutor, Chairman, KnowledgeKeeper, Operator
├── Debate/               ← StandardDebate, CritiqueDebate, ConsensusDebate
├── Orchestration/        ← IDebateOrchestrator, LocalDebateOrchestrator, DebateHandle, DebateRoundEvent
├── Cache/                ← IDebateCache, InMemoryDebateCache, FileDebateCache, CacheBehavior, DebateCacheKeyGenerator
├── Compression/          ← Semantic / Deduplication / Summarization / Hybrid
├── Chunking/             ← AutoChunker, AutoChunkingOrchestrator, AutoChunkingOptions
├── Providers/
│   ├── LLM/              ← OllamaProvider, ChatClientLLMProvider, EmbeddingGeneratorProvider
│   ├── RAG/              ← QdrantRagProvider, PgVectorRagProvider
│   └── Mcp/              ← McpClientAdapter (Operator ↔ MCP-серверы)
├── Extensions/           ← MicrosoftAIExtensions (мосты IChatClient ↔ ILLMProvider)
├── DependencyInjection/  ← AddDelibera() / AddDeliberaChatClient() + CouncilOptions
├── Knowledge/            ← MarkdownKnowledgeBase
├── Models/               ← CouncilMember, DebateResult, DebateRound, TokenStatistics, ...
└── Interfaces/           ← ILLMProvider, IRagProvider, IContextCompressor, IOperator, IMcpClient, ...

Delibera.Redis
├── Orchestration/        ← RedisDebateOrchestrator, DebateWorkerService, RedisOrchestratorOptions, RedisOrchestratorExtensions
└── Cache/                ← RedisDebateCache

Delibera.Server
├── Endpoints/            ← Minimal API эндпоинты (POST /api/debates, GET /stream и т.д.)
├── Services/             ← DebateOrchestrationService, SseDebateStreamWriter
└── Configuration/        ← AddDeliberaServer(), DeliberaServerOptions
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
