# Scenario API

The Scenario API lets you run a fully ad-hoc AI council from a single JSON payload — no registered template required.
Every aspect of the council (members, roles, strategy, voting, chairman, knowledge) is declared inline.

## Endpoints

| Method | URL | Description |
|--------|-----|-------------|
| `POST` | `/api/v1/scenarios` | Run synchronously; waits for completion |
| `POST` | `/api/v1/scenarios/async` | Fire-and-forget; poll `GET /api/v1/debates/{id}` |
| `POST` | `/api/v1/scenarios/validate` | Validate JSON without executing |
| `GET`  | `/api/v1/scenarios/{id}/stream` | SSE stream of live `DebateRound` events |

> Scenario debates share the same `DebateRecord` store as template debates.  
> Use `GET /api/v1/debates/{id}` and `GET /api/v1/debates/{id}/result` to fetch results.

---

## ScenarioRequest schema

```jsonc
{
  // ── Required ─────────────────────────────────────────────────────────────
  "question": "Should we adopt event sourcing for the Orders domain?",
  "members": [
    {
      "role": "Architect",          // display name used in transcript
      "model": "qwen2.5:7b",        // omit → server default strong model
      "provider": "Ollama",         // Ollama | OpenAI | AzureOpenAI | Anthropic
      "persona": "Senior architect focused on maintainability and CQRS patterns.",
      "weight": 2.0,                // used when votingStrategy = "Weighted"
      "capabilities": "Text"        // Text | Vision | Both
    },
    {
      "role": "DevLead",
      "persona": "Pragmatic dev lead. Weighs team skill and delivery risk.",
      "weight": 1.5
    },
    {
      "role": "Ops",
      "persona": "Platform engineer. Focus on operational complexity and observability."
    }
  ],

  // ── Optional: council configuration ──────────────────────────────────────
  "strategy": "Critique",           // Standard | Critique | Consensus  (default: Standard)
  "maxRounds": 3,                   // default: 3
  "temperature": 0.7,               // default: 0.7
  "votingStrategy": "Weighted",     // Majority | BordaCount | Weighted  (default: no voting)
  "memberWeights": {                // override per-member weights map
    "Architect": 2.0,
    "DevLead": 1.5,
    "Ops": 1.0
  },

  // ── Optional: chairman ────────────────────────────────────────────────────
  "chairman": {
    "model": "qwen2.5:7b",
    "systemPrompt": "Synthesise the debate into a structured JSON verdict: { Decision, Rationale }.",
    "openingStatement": false
  },

  // ── Optional: context & knowledge ────────────────────────────────────────
  "systemPrompt": "You are part of the Architecture Committee.",
  "knowledgeText": "Current stack: .NET 10, PostgreSQL, RabbitMQ.",
  "corpusIds": ["adr-corpus-01"],
  "inputData": { "currentArchitecture": "CRUD + direct DB" },

  // ── Optional: structured output ───────────────────────────────────────────
  "outputSchema": "{\"type\":\"object\",\"properties\":{\"Decision\":{\"type\":\"string\"},\"Rationale\":{\"type\":\"string\"}}}",

  // ── Optional: misc ────────────────────────────────────────────────────────
  "compressionStrategy": "None",    // Hybrid | Semantic | Deduplication | Summarization | None
  "label": "event-sourcing-adr-2026"
}
```

---

## Examples

### Minimal — two members, no chairman

```bash
curl -X POST http://localhost:8080/api/v1/scenarios \
  -H 'Content-Type: application/json' \
  -d '{
    "question": "Should we migrate from REST to gRPC for internal services?",
    "members": [
      { "role": "Proponent", "persona": "Advocate for gRPC: performance and strong contracts." },
      { "role": "Skeptic",   "persona": "Devil advocate: REST simplicity and broader tooling." }
    ],
    "strategy": "Critique",
    "maxRounds": 2
  }'
```

### Full — chairman + weighted voting + inline knowledge

```bash
curl -X POST http://localhost:8080/api/v1/scenarios \
  -H 'Content-Type: application/json' \
  -d '{
    "question": "Which caching strategy should we adopt for the product catalogue?",
    "members": [
      { "role": "Architect", "persona": "Distributed systems expert.",    "weight": 2.0 },
      { "role": "Backend",   "persona": "API developer, Redis fan.",       "weight": 1.5 },
      { "role": "DBA",       "persona": "Database performance specialist.","weight": 1.0 }
    ],
    "strategy": "Consensus",
    "maxRounds": 4,
    "votingStrategy": "Weighted",
    "chairman": {
      "systemPrompt": "Produce a JSON verdict: { Decision, Rationale, Risks[] }"
    },
    "knowledgeText": "Current infra: .NET 10 API, PostgreSQL 16, Redis 7 available."
  }'
```

### Validate only (no execution)

```bash
curl -X POST http://localhost:8080/api/v1/scenarios/validate \
  -H 'Content-Type: application/json' \
  -d '{ "question": "Test", "members": [{ "role": "A" }] }'
# → { "valid": true, "memberCount": 1, "strategy": "Standard", "maxRounds": 3, ... }
```

### Fire-and-forget + SSE stream

```bash
# 1. Enqueue
RESPONSE=$(curl -s -X POST http://localhost:8080/api/v1/scenarios/async \
  -H 'Content-Type: application/json' \
  -d '{ "question": "...", "members": [...] }')
ID=$(echo $RESPONSE | jq -r '.debateId')

# 2. Stream rounds live via SSE
curl -N http://localhost:8080/api/v1/scenarios/$ID/stream

# 3. Fetch final result
curl http://localhost:8080/api/v1/debates/$ID/result
```

---

## Scenario vs Template

| | Template (`POST /debates`) | Scenario (`POST /scenarios`) |
|---|---|---|
| Member roles | Fixed by template | Any roles you define |
| Strategy | Fixed by template | Standard / Critique / Consensus |
| Voting | Fixed by template | Any or none |
| Chairman prompt | Fixed by template | Free-form or omit |
| Registration required | Yes (`templateId`) | No |
| Structured output | Via template verdict type | Via `outputSchema` JSON string |
| Best for | Repeatable, governed councils | One-off, CI pipelines, SDK clients |
