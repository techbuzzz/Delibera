# I-00 · Project Vision — Hypotheses & OSS Growth Plan

> **Branch:** `feature/v10.3.0`  
> **Author:** AI assistant (with @techbuzzz input)  
> **Date:** July 2026

Этот документ фиксирует продуктовые гипотезы и план развития Delibera как заметного проекта в open‑source экосистеме. Цель — дать контекст для фич 10.3.0–10.5.0 и облегчить передачу работы другим участникам.

---

## 1. Контекст

Delibera — .NET‑фреймворк для AI‑советов (multi‑agent councils) поверх любых LLM‑провайдеров. Ядро уже включает:

- Council / Chairman / DebateResult модель
- Стратегии дебатов (Standard, Critique, Consensus, Adaptive)
- Voting (Majority, Borda, Weighted)
- RAG (Qdrant, pgvector), structured output, persistence, telemetry

Ветка `feature/v10.3.0` превращает библиотеку в платформу: Server, gRPC, NuGet GA.[1]

---

## 2. Продуктовые гипотезы

### Гипотеза 1. Delibera как decision‑layer поверх любых LLM

Если позиционировать Delibera как независимый "decision layer" (совет + дебаты + голосование) поверх любых моделей, то:

- Интеграторы и internal‑платформы будут использовать его как общий модуль deliberation.
- Переход между OpenAI / Azure / Anthropic / локальными моделями не потребует переписывать логику принятия решений.

**Следствия для API:**

- Стабильные абстракции: `ILLMProvider`, `ICouncilExecutor`, `IDebateOrchestrator`.
- Чёткое разделение: core (`Delibera.Core`) vs transports (`Delibera.Server`, `Delibera.Grpc`).

### Гипотеза 2. Benchmarks & evaluation привлекут исследователей

Если дать встроенные инструменты A/B‑тестирования конфигураций council’ов (разные стратегии, составы, модели), Delibera станет удобным инструментом для тех, кто изучает multi‑agent debate.

- I‑02 Debate Diff → структурный diff двух DebateResult.
- I‑03 CLI `benchmark` → запуск N конфигураций на одном наборе вопросов.

### Гипотеза 3. Delibera как "AI‑советник" для других OSS‑проектов

Если показать, как Delibera помогает принимать архитектурные и продуктовые решения для других open‑source проектов, появится органический интерес.

- Architecture Council для чужих репозиториев (RFC, ADR).
- Публикация реальных decision‑log’ов (сгенерированных Delibera) в README / docs этих проектов.

---

## 3. Гипотезы по росту видимости в open source

### Гипотеза 4. Reference‑имплементации вокруг популярных стеков

Если сделать официальные адаптеры и примеры для .NET, Python, JS/TS, Delibera начнут использовать как строительный блок:

- .NET: CLI, Server, Templates (уже в 10.3.0/10.4.0).
- Python: тонкий HTTP/gRPC‑клиент + примеры с LangChain/LlamaIndex.
- Node/TS: клиент и пример для Next.js/NestJS.

### Гипотеза 5. Бренд "AI Councils" как ниша

Если последовательно продвигать идею "AI‑совет" (AI Council) и закрепить Delibera как фреймворк для их построения, проект может занять нишу между general‑purpose orchestrators и узкими агентными библиотеками.

Практически:

- README, NuGet description, GitHub topics → единое позиционирование.
- Серия статей "Patterns for AI Councils with Delibera".

### Гипотеза 6. Интеграции с IDE/AI‑клиентами

Если Delibera легко подключается к Claude/Cursor/VS Code как внешний backend, разработчики смогут использовать "советы" прямо в IDE.

- MCP endpoint уже реализован в `Delibera.Server` (feature/v10.3.0).
- Следующий шаг — документация и примеры подключения к Claude Desktop / Cursor.

---

## 4. Community & contributions

### Гипотеза 7. Vertical playbooks стимулируют вклады

Если оформить вертикали (Engineering, Governance, Legal, Product) как "playbooks" и шаблоны, внешним контрибьюторам проще предлагать свои council templates и сценарии.

Идеи:

- `docs/playbooks/Engineering.md`, `Governance.md`, `Legal.md`, `Product.md`.
- Отдельные labels в issues: `vertical:engineering`, `vertical:governance` и т.п.

### Гипотеза 8. Живой roadmap и релиз‑ноты

Если roadmap (10.4, 10.5 и дальше) и `WhatsNew-*` поддерживать актуальными, проект выглядит живым и предсказуемым для пользователей.[1]

- Поддерживать `docs/ROADMAP.md` в синхронизации с issues и ветками.
- Для каждого релиза делать GitHub Release + `WhatsNew-vX.Y.Z.md`.

### Гипотеза 9. Рецепты решений (decision recipes)

Если регулярно публиковать готовые рецепты (architecture, risk, product decisions) поверх Delibera, формируется аудитория, которая приходит за практикой, а не только за кодом.

Формат:

- "Architecture Council recipe: миграция монолита на микросервисы".
- "Risk Council recipe: оценка нового AI‑фичера на соответствие регуляторике".

---

## 5. Связка гипотез с roadmap (10.3.0–10.5.0)

### v10.3.0 — Platform Release (P‑01..P‑04)

Фокус: сделать Delibera не только библиотекой, но и платформой.[1]

- P‑01 Breaking‑change cleanup → чистый публичный API для дальнейшего роста.
- P‑02 Delibera.Server → REST+SSE сервер как стандартный способ интеграции.
- P‑03 Delibera.Grpc → high‑throughput, strongly‑typed интеграции для internal сервисов.
- P‑04 NuGet GA → стабильные пакеты `Delibera.Core`, `Server`, `Grpc`, `Templates`.

**Поддерживаемые гипотезы:**

- H1 (decision‑layer) — API и пакеты стабилизированы.
- H4 (reference‑имплементации) — .NET‑стек закрыт "из коробки".
- H8 (живой roadmap) — 10.3.0 как крупная "опорная" версия.

### v10.4.0 — Intelligence & DX (I‑01..I‑03)[1]

Фокус: сделать советы умнее и облегчить жизнь разработчикам.

- I‑01 Function Calling / Tool Use → поддержка инструментов через `AIFunction` + MCP.
- I‑02 Debate Result Diff → A/B‑сравнение стратегий и конфигураций.
- I‑03 CLI → `run`, `resume`, `compare`, `benchmark` для DevEx и CI/CD.

**Поддерживаемые гипотезы:**

- H2 (benchmarks & evaluation) — diff + benchmark CLI.
- H4 (reference‑имплементации) — CLI как интерфейс для Python/JS пользователей.
- H7 (playbooks) — удобнее поставлять готовые `*.json` сценарии и примерные команды CLI.

### v10.5.0 — Scale & Reliability (S‑01..S‑03)[1]

Фокус: масштаб и контроль стоимости.

- S‑01 Distributed Debates → Redis‑оркестрация раундов по воркерам.
- S‑02 Rate‑Limiting & Cost Gates → `WithCostLimit`, `WithRateLimit`, `DebateResult.CostEstimate`.
- S‑03 Result Caching → `IDebateCache` + File/InMemory/Redis реализации.

**Поддерживаемые гипотезы:**

- H1 (decision‑layer) — production‑grade масштабирование.
- H6 (интеграции) — устойчивость к rate limits провайдеров.
- H9 (recipes) — можно делиться сценариями без страха по стоимости (есть гейты и кэш).

---

## 6. План действий (последовательно)

### Шаг 1 — Завершить 10.3.0

- [ ] Доделать P‑03 (gRPC) по spec из `docs/ROADMAP.md` и issue [#11].
- [ ] Выполнить P‑04: NuGet GA, SourceLink, CI‑workflow для релизов.
- [ ] Обновить `README`/`Server.md` с акцентом на "Delibera как платформа".

### Шаг 2 — Подготовить foundation для 10.4.0

- [ ] Спроектировать `IToolProvider` и интеграцию с `AIFunction` (I‑01).
- [ ] Спроектировать `DebateDiff` + Markdown/HTML вывод (I‑02).
- [ ] Спроектировать структуру `Delibera.Cli` (I‑03) и варианты дистрибуции (`dotnet tool`).

### Шаг 3 — Community & контент

- [ ] Создать раздел GitHub Discussions: `Show & Tell`, `Templates`, `Questions`.
- [ ] Подготовить 1–2 "decision recipes" по архитектуре и рискам.
- [ ] Добавить labels `vertical:*` и `good first issue` к небольшим задачам.

### Шаг 4 — Подготовка 10.5.0

- [ ] Проработать контракт `IDebateOrchestrator` и общий Redis‑layout (общий для S‑01 и S‑03).
- [ ] Определить модель pricing registry для S‑02 (отдельный конфиг в `appsettings.json`).
- [ ] Решить, какие метрики по стоимости и rate limiting будут обязательными в telemetry.

---

## 7. Риски и как их контролировать

**Риск 1. Размывание фокуса (слишком много направлений)**

- Митигация: держаться roadmap 10.3–10.5, не принимать большие фичи вне заявленных тем.

**Риск 2. Сложность онбординга новых контрибьюторов**

- Митигация: чёткий QuickStart, CLI, примеры, labels `good first issue`, vertical playbooks.

**Риск 3. Стоимость LLM для демо и development**

- Митигация: S‑02/S‑03 (cost gates, caching), локальные модели через Ollama/LM Studio.

---

## 8. Как пользоваться этим документом

- Для новых фич → проверять, какую гипотезу они поддерживают.
- Для PR‑ов → ссылаться на разделы (2–6) в описании, чтобы было видно, как вклад ложится в общую картину.
- Для обсуждений в Issues/Discussions → использовать термины из этого документа (AI Council, decision‑layer, playbooks, recipes), чтобы держать консистентный язык.
