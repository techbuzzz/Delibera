<template>
  <div v-if="record.status === 'Completed' || record.verdict || record.finalVerdict" class="panel">
    <h2 class="panel__title">Verdict</h2>

    <p v-if="record.errorMessage" class="error">{{ record.errorMessage }}</p>

    <p v-if="recommendation" class="verdict">{{ recommendation }}</p>
    <p v-else-if="record.finalVerdict" class="verdict">{{ record.finalVerdict }}</p>

    <dl v-if="record.durationMs != null || record.tokenStats || record.voting" class="stats">
      <div v-if="record.durationMs != null" class="stat">
        <dt>Duration</dt>
        <dd>{{ (record.durationMs / 1000).toFixed(1) }}s</dd>
      </div>

      <template v-if="record.tokenStats">
        <div class="stat">
          <dt>Total tokens</dt>
          <dd>{{ record.tokenStats.totalTokens.toLocaleString() }}</dd>
        </div>
        <div class="stat">
          <dt>Input / output</dt>
          <dd>
            {{ record.tokenStats.totalInputTokens.toLocaleString() }} /
            {{ record.tokenStats.totalOutputTokens.toLocaleString() }}
          </dd>
        </div>
        <div v-if="record.tokenStats.savedByCompression > 0" class="stat">
          <dt>Saved by compression</dt>
          <dd>{{ record.tokenStats.savedByCompression.toLocaleString() }}</dd>
        </div>
      </template>

      <div v-if="record.voting" class="stat">
        <dt>Strategy</dt>
        <dd>{{ record.voting.strategy }}</dd>
      </div>
      <div v-if="record.voting" class="stat">
        <dt>Winner</dt>
        <dd>{{ record.voting.winner }}</dd>
      </div>
    </dl>

    <!--
      `verdict.rawJson` is the only place template-specific structured output actually
      appears: DebateMapper populates nothing but `recommendation` and `rawJson`, so
      `confidence`, `riskLevel`, `risks` and `conditions` are permanently absent and must
      not be rendered. The payload is shown as formatted JSON rather than mapped to fields.
    -->
    <details v-if="rawJson" class="details">
      <summary>Structured output ({{ record.verdict?.rawJson ? 'rawJson' : '' }})</summary>
      <pre class="json">{{ prettyRaw }}</pre>
    </details>

    <p v-if="record.cacheHit" class="cache">Served from cache.</p>
  </div>
</template>

<script setup lang="ts">
import type { DebateResponse } from '#shared/types/delibera'

const props = defineProps<{ record: DebateResponse }>()

const recommendation = computed(
  () => props.record.verdict?.recommendation ?? props.record.finalVerdict ?? null,
)

const rawJson = computed(() => props.record.verdict?.rawJson ?? null)

const prettyRaw = computed(() => {
  const raw = rawJson.value
  if (!raw) return ''
  // rawJson is already a JSON string from the server; pretty-print when parseable, and fall
  // back to the raw text rather than swallowing it.
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
})
</script>

<style scoped>
.panel {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 1rem 1.15rem;
  margin-bottom: 1.25rem;
}

.panel__title {
  margin: 0 0 0.75rem;
  font-size: 1rem;
  color: var(--text-soft);
}

/* The verdict is the one piece of prose the reader came for, so it is set in the serif at a
   larger size than any other body text and capped to a readable measure. Everything else on
   this panel is instrumentation and stays in the interface face. */
.verdict {
  margin: 0 0 1rem;
  font-family: var(--font-serif);
  font-size: 1.05rem;
  line-height: 1.65;
  max-width: 68ch;
  white-space: pre-wrap;
}

.error {
  margin: 0 0 0.75rem;
  color: var(--danger-fg);
  font-size: 0.85rem;
}

.stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 0.75rem;
  margin: 0 0 0.75rem;
}

.stat {
  background: var(--surface-2);
  border-radius: var(--radius-sm);
  padding: 0.5rem 0.65rem;
  /* Grid items default to `min-width: auto`, which lets a long value push the track wider than
     its share and spill over the neighbouring stat. `ArchitectureReviewStrategy` is a CLR type
     name and is the longest value this panel ever shows. */
  min-width: 0;
}

.stat dt {
  font-size: 0.68rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

/* Readouts, not prose: fixed pitch so a six-digit token count lines up with the one beside it
   instead of drifting a few pixels. */
.stat dd {
  margin: 0.15rem 0 0;
  font-family: var(--font-mono);
  font-size: 0.9rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  /* Breaks inside an identifier rather than overflowing it. Numbers never need this; names do. */
  overflow-wrap: anywhere;
}

.details summary {
  cursor: pointer;
  font-size: 0.8rem;
  color: var(--text-soft);
}

.json {
  margin: 0.6rem 0 0;
  padding: 0.75rem;
  background: var(--surface-3);
  border-radius: var(--radius-sm);
  font-family: var(--font-mono);
  font-size: 0.75rem;
  line-height: 1.5;
  overflow-x: auto;
  max-height: 380px;
}

.cache {
  margin: 0.5rem 0 0;
  font-size: 0.72rem;
  color: var(--ok-fg);
}
</style>