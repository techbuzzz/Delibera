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
  background: #151922;
  border: 1px solid #262b36;
  border-radius: 8px;
  padding: 1rem 1.15rem;
  margin-bottom: 1.25rem;
}

.panel__title {
  margin: 0 0 0.75rem;
  font-size: 1rem;
  color: #9fb0d0;
}

.verdict {
  margin: 0 0 1rem;
  font-size: 1rem;
  line-height: 1.6;
  white-space: pre-wrap;
}

.error {
  margin: 0 0 0.75rem;
  color: #ff9a9a;
  font-size: 0.85rem;
}

.stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 0.75rem;
  margin: 0 0 0.75rem;
}

.stat {
  background: #1b202b;
  border-radius: 6px;
  padding: 0.5rem 0.65rem;
}

.stat dt {
  font-size: 0.68rem;
  color: #7d879c;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.stat dd {
  margin: 0.15rem 0 0;
  font-size: 0.9rem;
  font-weight: 600;
}

.details summary {
  cursor: pointer;
  font-size: 0.8rem;
  color: #9fb0d0;
}

.json {
  margin: 0.6rem 0 0;
  padding: 0.75rem;
  background: #0d1016;
  border-radius: 6px;
  font-size: 0.75rem;
  line-height: 1.5;
  overflow-x: auto;
  max-height: 380px;
}

.cache {
  margin: 0.5rem 0 0;
  font-size: 0.72rem;
  color: #7ee2a8;
}
</style>