# Measured behaviour

Everything on this page was measured against **Ollama Cloud** (`https://api.ollama.com`) on
2026-10-06 with real API keys, not estimated. Sections 1-5 exercise the library through harnesses;
section 6 measures `Delibera.Server` running as a container. Reproduce with the harnesses described
at the bottom.

Numbers are reported as observed. Where a result contradicted a documented claim, the claim is
flagged rather than repeated.

---

## 1. Model latency and throughput

Five of the cheapest models, 3 measured calls each, 256 generated tokens per call.
`TTFT` is client-side time to first token; `tok/s` is observed throughput including network.

| model | TTFT p50 | total p50 | tok/s (net) | server tok/s |
|---|---:|---:|---:|---:|
| `gemma4` | 391 ms | 1,126 ms | 92 | n/a |
| `gpt-oss:120b` | 583 ms | 1,123 ms | 302 | n/a |
| `glm-5.3-flash` | 2,943 ms | 3,197 ms | 616 | n/a |
| `gpt-oss:20b` | 2,611 ms | 3,278 ms | 384 | n/a |
| `nemotron-3-nano` | — | — | — | — |

Two things this table proves:

- **`server tok/s` is `n/a` for every model.** Ollama Cloud does not return `eval_duration` in
  `/api/chat`, so server-side throughput cannot be measured through the API at all. Reporting a
  number there would be fabrication.
- **`nemotron-3-nano` returns HTTP 404**, because the pricing page uses the short name while the
  endpoint serves `nemotron-3-nano:30b`. Model ids from `GET /api/tags` are authoritative, not
  the ones in the pricing table.

### Reasoning models return empty answers

With a 1,024-token generation budget, `gpt-oss:20b` and `glm-5.3-flash` intermittently returned
**zero characters** after spending the full budget on internal reasoning — and the tokens were
billed regardless. `minimax-m2.7` does the same. Raising the budget to 2,048 then 3,072 moved
failures from 3 to 3 to 2 while cost doubled and wall time tripled, so a bigger budget is not the
fix. See [CHANGELOG 10.5.0](../CHANGELOG.md) for what the library now does about it.

---

## 2. Debate overhead

Full matrix: **5 topics x {2, 4} rounds x {compression off, on} = 20 runs**, 3 members plus a
chairman. $0.12 total, 32.6 s mean wall time per run.

**On call count.** The harness reported **234 calls, 88 of which returned no answer** — but that
log predates the recorder fix, so both figures are inflated. Every empty answer was written twice:
once in the success path with its real token counts, and once again in the `catch` block, which
records zero tokens. The evidence is in the report's own failed-call table: its 88 rows split
exactly **44 billing 1,024 output tokens and 44 billing 0**. One call cannot bill both, so these
are two records of the same call. Real counts are therefore **190 model calls, 44 of them empty**.

The table below is a **separate 3-topic, 4-round quality run** (11 calls each: 3 members x 3 rounds
plus a chairman opening and a verdict), not a slice of the 20-run matrix. It is quoted here because
those are the runs whose full transcripts were kept and read.

| Topic | Wall | Output chars | chars/s | Member fan-out overlap |
|---|---:|---:|---:|---:|
| code-review | 69.8 s | 78,365 | 1,123 | 1.41x |
| architecture | 119.0 s | 136,215 | 1,144 | 1.56x |
| contested | 238.5 s | 200,339 | 840 | 1.23x |

**The framework itself is not the bottleneck.** Time no model call explains: **0.0 s** in every run.
Throughput is flat at 840–1,144 chars/s across all nine phase-runs, so wall time tracks how much
text the models write, not anything Delibera adds.

Where the time actually goes, per round:

| topic | R1 initial | R2 critique | R3 improved | R4 verdict |
|---|---:|---:|---:|---:|
| code-review | 15% | **63%** | 14% | 6% |
| architecture | 26% | **41%** | 21% | 12% |
| contested | 16% | 37% | **40%** | 6% |

Critique and refinement are **62–77%** of every debate. Both rounds carry the accumulated
transcript in their prompt, so cost compounds.

**Fan-out overlap is 1.23–1.56x out of a possible 3x.** Members are dispatched in parallel, but a
round lasts as long as its slowest participant, and the roster mixes model sizes.

### One participant dominated the output

From the same 3-topic, 4-round quality run as the table above: nine member answers per model
(3 member rounds x 3 debates), measured off the saved transcripts.

| model | answers | avg chars | max | total | share of member output |
|---|---:|---:|---:|---:|---:|
| `gemma4` | 9 | 3,909 | 5,225 | 35,181 | 9.1% |
| `gpt-oss:120b` | 9 | 9,501 | 13,894 | 85,512 | 22.1% |
| `glm-5.3-flash` | 9 | **29,571** | **60,527** | 266,137 | **68.8%** |

Member output totals **386,830 characters**, which matches the harness's own rounds 1-3 row sum
(386,829 after per-answer rounding) — the table reconciles to the source rather than sitting beside it.

`glm-5.3-flash` produced **68.8% of member output** — 3.1x the next model and 7.6x `gemma4`. It
does not separate
reasoning from answering: with `think: false` its analysis still arrives inline, opening answers
with "Let me analyze the code" and re-quoting the source before the actual reply. The council then
reads, quotes and critiques that self-analysis in every later round. Dropping it from the roster
removed 68.8% of the text at a stroke.

---

## 3. Context compression

Before 10.5.0 compression was configured but never invoked. Ten runs with compression enabled
reported **0.00% saved at 0 ms overhead**, while context grew to 10,615 prompt tokens in a single
round.

After the fix, three topics:

| topic | original | compressed | saved |
|---|---:|---:|---:|
| retry-policy | 9,271 | 8,155 | **12.0%** |
| cancellation | 11,322 | 9,947 | **12.1%** |
| latency-slo | 11,553 | 10,199 | **11.7%** |

**This contradicts the advertised range.** README and the compression docs have claimed 30–70%.
Measured `HybridCompressor` yield is a tight **11.7–12.1%**, roughly a sixth of the lower bound.
The wiring fix is what made the feature work at all; the strategy's actual saving is much smaller
than the documentation states, and that documentation needs correcting rather than repeating.

---

## 4. Knowledge Keeper and Operator

Three debates with RAG over 24 chunks of real documentation (Polly retry and pipelines from
context7, `CancellationToken` from Microsoft Learn), plus `chrome-devtools-mcp` wired to the Operator:

| topic | wall | knowledge queries | operator tasks / tool calls | failed member turns |
|---|---:|---:|---:|---:|
| retry-policy | 208.7 s | 3 | 1 / 2 | 0 |
| cancellation | 172.9 s | 3 | 3 / 8 | 0 |
| latency-slo | 154.3 s | 3 | 2 / 10 | 0 |

Total: $0.00314, no debate degraded.

- **Knowledge Keeper is steady at 3 queries per debate** regardless of topic, which makes retrieval
  cost predictable enough to budget for.
- **Operator effort scales with the question**: 1 task / 2 tool calls on a topic the corpus
  already answers, up to 3 tasks / 10 tool calls when the question genuinely needs looking
  something up.

Retrieval quality: a probe for *"which failures are transient and retryable?"* returned 2 hits with
a top score of 0.738, and the resulting verdicts cite specific documents with line ranges.

### Verdicts are grounded, and say so when they are not

The earlier cheap-roster run produced a verdict that **invented** unanimity — "all three
participants recognised that the N+1 query pattern is the dominant scalability defect" — in a
document whose own Dissent section said one expert had missed N+1 entirely. That was the worst
quality failure observed.

With a larger roster and a retrieval corpus, checking each verdict against the transcript:

| topic | consensus grounded? | dissent attributed to named experts? |
|---|---|---|
| retry-policy | yes | yes |
| latency-slo | **verified against the transcript** | yes |

The `latency-slo` claim "do not hedge" was checked directly: Expert 1's answer opens with
`# Should you hedge? No` and argues it at length. The Chairman also reports the limits of its own
evidence — *"Some provided verbatim-style quotes that could not be verified in session; others only
paraphrased"* — rather than presenting unverified citations as fact.

Three topics cannot establish a rate, and two of the three were not checked against the transcript
as deeply as `latency-slo`.

---

## 5. Caching

`RedisDebateCache` against Redis 7. An identical debate rerun returns `CacheHit=True` with a
deterministic key (`delibera:state:cache:03754963B3304CDF`) that is stable across processes.

| | wait time |
|---|---:|
| first run (uncached) | 154–209 s |
| rerun (cache hit, measured with a stopwatch) | **0.0 s** |

`DebateResult.TotalDuration` reports **189.0 s on both** — on a cache hit the result carries the
cached debate's timestamps. Anything computing latency or telemetry from that property would
overstate a sub-second lookup by roughly two orders of magnitude. Use `CacheHit` / `CachedAt`.

---

## 6. Delibera.Server

Measured against the container: `docker compose -f docker-compose.yml up -d`, published on
`localhost:5200`, with Redis and Qdrant alongside and models on Ollama Cloud (chat) plus a local
`nomic-embed-text` (embeddings — Ollama Cloud serves none).

### Transport and control surface

Everything here is a stopwatch reading against a running container.

| Check | Surface | ms |
|---|---|---:|
| `GET /health` | REST | 110 |
| `GET /templates` (5 registered) | REST | 36 |
| `POST /corpora` | REST | 37 |
| `POST /corpora/{id}/documents` (1 real chunk written) | RAG ingest | 321 |
| `POST /mcp` initialize | MCP | 132 |
| `POST /mcp` tools/list (6 tools) | MCP | 46 |
| `POST /mcp` tools/call | MCP | 52 |

Control-plane calls answer in **36-321 ms**. Nothing in the request path is expensive; the entire
server cost is deliberation time, below.

### Debates over HTTP

Completed records resident at the moment this table was taken — n=8:

| | ms |
|---|---:|
| fastest | 125,308 |
| slowest | 601,903 |

By template: `risk-committee` n=3, 125,308–178,003 ms; `code-review` n=5, 202,968–601,903 ms.

A median is deliberately not quoted: the population moves as records are evicted, so the median of
this set changes every time a debate is added. The range and the per-template split are the stable
statements.

The spread is the template talking, not the server: `code-review` seats five members and
`risk-committee` three, and rounds fan out in parallel, so round cost tracks member count.

A full record with token accounting, `risk-committee` / grounded in a corpus:

| | |
|---|---|
| `durationMs` | 155,309 |
| rounds | 4 |
| prompt tokens | 10,136 |
| output tokens | 11,813 |
| saved by compression | 5,105 |

> **Round count caveat.** This record ran **4** rounds. The request asked for 2 — but
> `CreateDebateRequest` has no top-level `maxRounds`; the only override is the nested
> `Options.MaxRounds`. A top-level key is not an error, it is simply ignored by the JSON binder, so
> the template fell back to its `DefaultMaxRounds` of 4. The override path is
> `"options": { "maxRounds": 2 }`.

**Caveat on this whole subsection:** completed records are evicted by a background timer
(`CompletedRecordLifetime`), so the set shrinks while you read. Figures here were captured from the
server's own `durationMs` at a point in time; re-running the harness produces a different sample,
not the same one.

### SSE

`GET /debates/{id}/stream` replays a debate that has already completed. Verified on a completed
4-round debate (82 KB of stream):

| order | event | content |
|---|---|---|
| 1 | `debate-round` | **all** rounds in a single frame |
| 2 | `debate-completed` | final payload, then the stream closes |

A live tail behaves differently: it emits **one `debate-round` per completed round** as they happen,
plus heartbeats, then `debate-completed`. So the two shapes are not interchangeable — a replay is
one big frame, a live subscription is many. Subscribe before the debate finishes if you want
progress; subscribing after gives you the whole thing at once.

A `Failed` debate streams `debate-error` and a cancelled one `debate-cancelled`; neither replays
rounds.

### What measuring the server found

Four defects, all fixed in this release. None were visible from the library harnesses in sections 1-5,
because they live in the server's configuration surface and in code the library never reaches.

1. **`createdAt` equalled `completedAt` on every debate.** The synchronous path built its record
   *after* `ExecuteAsync` returned, and `CreatedAt` defaulted to an object initialiser, so a 41-second
   debate reported zero duration. `DebateResponse` now carries `DurationMs`. Verified: a debate
   measured at 110.2 s reported 110,038 ms.
2. **The built-in templates could not reach any model the operator configured.** Five templates read
   `Delibera:Models:Fast` / `Delibera:Models:Strong`, while compose published only
   `Delibera__Providers__DefaultModel`, which templates never read. Ten code references, zero
   configuration mappings — so the server started healthy and then returned HTTP 500 on the first real
   debate with `HTTP error talking to Ollama (model: qwen2.5:7b): 404`.
3. **Qdrant was permanently unhealthy.** Its healthcheck called `wget`, and the `qdrant/qdrant` image
   ships bash but no wget, curl or nc, with `/bin/sh` as dash (no `/dev/tcp`). The server only started
   at all because the dependency is marked `required: false`, so one of two backing stores failing
   was invisible.
4. **RAG was configured but inert.** `IRagProvider` was never resolvable from DI, the corpus API
   computed a chunk estimate without embedding or storing anything, and no template attached a
   Knowledge Keeper — although all five declared `RagEnabled => true`. Now fixed: real indexing, one
   collection per corpus, and a keeper attached whenever RAG is on and the request names corpora.

After the fixes, a corpus-grounded `risk-committee` debate produced **4 knowledge interactions —
one in every round**, including the Chairman's. Verified twice: once on `5e5faa57…` and again on a
fresh `bd4eb00b…` (178,003 ms, 4 rounds) after that earlier record had been evicted. In both cases
`GET /debates/{id}/rounds` reported `knowledgeInteractions = 1` for each of rounds 1-4.

`operatorInteractions` was 0 on every debate measured: the server attaches a Knowledge Keeper, not
an Operator, so the MCP tooling in section 4 is not reachable through this surface.

### A smaller finding

A rejected request still leaves a `Failed` debate record behind: five `Failed` records of ~145 ms
each were present in `GET /debates` for requests that never passed validation. Harmless today, but a
polluting caller would misreport its rejection rate.

---

## Reproducing

| harness | what it measures |
|---|---|
| `.bench/OllamaCloudBench` | per-model TTFT, throughput, token counts and cost |
| `.bench/DebateBench` | full debate transcripts, per-round timing, per-model output volume, verdict quality |
| `.bench/DeepBench` | compression, RAG, Operator/MCP, Redis, end-to-end feature matrix |
| `.bench/ServerBench/Measure-Server.ps1` | `Delibera.Server` over REST, SSE and MCP plus the corpus ingest path |

Each needs a key in `Bench:OllamaCloud:ApiKey` via `dotnet user-secrets set`. `DeepBench` also
expects Redis and Qdrant in Docker and a local `nomic-embed-text` for embeddings — Ollama Cloud
offers no embedding model. Harnesses live under `.bench/`, which is excluded from Git, CI and the
solution.

The server harness is a PowerShell script rather than a project, so it runs with no build step. Two
things about it are worth knowing before debugging a failure from it:

- **POSTs go through `curl.exe`, not `Invoke-RestMethod`.** Windows PowerShell 5.1 rejects these
  JSON bodies with a bare HTTP 400 — verified as client-side, since the identical request via curl
  returns 202. It looks like an API defect and is not.
- **Drive `/debates/async`, not `/debates`.** The blocking endpoint holds the connection for the whole
  deliberation, which measures as one opaque number and overruns client timeouts.

### The one harness bug in these numbers

`DebateBench`'s recorder wrote every empty answer **twice** — once in the success path with its real
token counts, and again in the `catch` block, which records zero tokens. Runs that predate the fix
therefore overstate both the call count and the empty-call count by exactly the number of failures.

You can see the pairing in any affected report's failed-call table: the rows come in twos, one
billing the real tokens and its partner billing 0. That is why §2 quotes 190 calls rather than the
234 printed by those older logs.

The fix routes every exit path through a single `Record` call. Verified afterwards by a pilot
reporting 8 calls with 1 failure — one row, not two.