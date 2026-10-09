<template>
  <section>
    <div class="header">
      <div>
        <h1 class="title">{{ record?.templateId ?? 'Debate' }}</h1>
        <p class="subtitle">
          <code>{{ debateId }}</code>
          <StatusBadge v-if="record" :status="record.status" />
          <span v-if="polling" class="badge badge--warn">live stream unavailable — polling</span>
          <span v-else-if="connected" class="badge badge--live">live</span>
        </p>
      </div>

      <div class="header__actions">
        <a class="btn" :href="exportHref" download>Export Markdown</a>
        <button
          v-if="record && isActive(record.status)"
          class="btn btn--danger"
          :disabled="cancelling"
          @click="onCancel"
        >
          {{ cancelling ? 'Cancelling…' : 'Cancel' }}
        </button>
      </div>
    </div>

    <p v-if="loadError" class="alert alert--error">
      {{ loadError.message }}
      <template v-if="loadError.status === 404">
        — this debate is unknown or its record has expired (records are evicted about 30
        minutes after completion).
      </template>
      <NuxtLink to="/" class="alert__link">Back to the list</NuxtLink>
    </p>

    <p v-if="streamError" class="alert alert--error">{{ streamError }}</p>

    <VerdictPanel v-if="record" :record="record" />

    <ClientOnly>
      <RoundTimeline :rounds="rounds" />
      <template #fallback>
        <p class="alert">Loading rounds…</p>
      </template>
    </ClientOnly>
  </section>
</template>

<script setup lang="ts">
import type { DebateResponse } from '#shared/types/delibera'
import { isActive } from '#shared/types/delibera'
import type { ParsedApiError } from '~/composables/useApiError'

const route = useRoute()
const { get, cancel, exportUrl } = useDebates()

// The id lives in the URL, so a reload recovers the view for as long as the server still
// holds the record (~30 minutes after completion).
const debateId = computed(() => String(route.params.id ?? ''))

const record = ref<DebateResponse | null>(null)
const loadError = ref<ParsedApiError | null>(null)
const cancelling = ref(false)

const { rounds, errorMessage: streamError, connected, polling, start } = useDebateStream()

const exportHref = computed(() => exportUrl(debateId.value))

async function loadRecord(): Promise<void> {
  try {
    record.value = await get(debateId.value)
    loadError.value = null
  } catch (error) {
    loadError.value = error as ParsedApiError
    record.value = null
  }
}

/**
 * Terminal SSE events carry no token stats, voting or duration, so the authoritative
 * record is re-fetched once the stream ends. This is also what populates the verdict panel.
 */
async function onStreamFinished(): Promise<void> {
  await loadRecord()
}

async function onCancel(): Promise<void> {
  cancelling.value = true
  try {
    const outcome = await cancel(debateId.value)
    // "already finished" is a success from the caller's point of view, not an error — the
    // server answers 404 for a debate that has already reached a terminal state.
    if (outcome === 'already-finished') {
      await loadRecord()
    } else {
      await loadRecord()
    }
  } catch (error) {
    loadError.value = error as ParsedApiError
  } finally {
    cancelling.value = false
  }
}

onMounted(async () => {
  await loadRecord()

  // Only stream while the debate can still change. A debate that was already terminal when
  // the page loaded has nothing to stream, and opening the SSE route anyway would just
  // receive the terminal replay.
  if (!record.value || isActive(record.value.status)) {
    start(debateId.value, onStreamFinished)
  }

  // Seed the timeline from the paginated endpoint so a completed debate shows its rounds
  // even if the stream has already closed.
  if (record.value && !isActive(record.value.status) && rounds.value.length === 0) {
    try {
      rounds.value.push(...(await useDebates().rounds(debateId.value)))
    } catch {
      // Rounds are supplementary; the verdict panel above is the authoritative result.
    }
  }
})
</script>

<style scoped>
.header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 1.25rem;
}

.title {
  margin: 0;
  font-size: 1.35rem;
}

.subtitle {
  margin: 0.35rem 0 0;
  display: flex;
  align-items: center;
  gap: 0.6rem;
  font-size: 0.78rem;
  color: #7d879c;
  flex-wrap: wrap;
}

.header__actions {
  display: flex;
  gap: 0.6rem;
}

.btn {
  background: #24314a;
  color: #dce6ff;
  border: 1px solid #33415c;
  border-radius: 6px;
  padding: 0.4rem 0.8rem;
  font-size: 0.85rem;
  cursor: pointer;
  text-decoration: none;
}

.btn--danger {
  background: #4a2020;
  border-color: #6b2c2c;
  color: #ff9a9a;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.alert {
  padding: 0.8rem 1rem;
  border-radius: 8px;
  background: #151922;
  border: 1px solid #262b36;
  color: #9fb0d0;
  margin: 0 0 1rem;
}

.alert--error {
  border-color: #6b2c2c;
  background: #241212;
  color: #ff9a9a;
}

.alert__link {
  color: #dce6ff;
  margin-left: 0.5rem;
}

code {
  background: #1c2230;
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
}
</style>