<template>
  <section>
    <div class="header">
      <div>
        <h1 class="title">{{ record?.templateId ?? 'Debate' }}</h1>
        <p class="subtitle">
          <code>{{ debateId }}</code>
          <StatusBadge v-if="record" :status="record.status" />
          <!-- Only claim polling when a record actually exists to poll; otherwise a 404
               record would show "live stream unavailable" indefinitely, which reads as a
               transport fault rather than the honest "this debate is gone". -->
          <span v-if="record && polling" class="badge badge--warn">live stream unavailable — polling</span>
          <span v-else-if="record && connected" class="badge badge--live"><i class="badge__pulse" aria-hidden="true" />live</span>
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

    <p v-if="visibleError" class="alert alert--error">
      {{ visibleError.message }}
      <template v-if="visibleError.status === 404">
        — this debate is unknown or its record has expired (records are evicted about 30
        minutes after completion).
      </template>
      <NuxtLink to="/" class="alert__link">Back to the list</NuxtLink>
    </p>

    <p v-if="streamError" class="alert alert--error">{{ streamError }}</p>

    <!-- Polite rather than assertive: the cancel button's own result is the user's cue, and
         interrupting whatever a screen reader is currently saying to state it would cost
         more than it communicates. -->
    <p v-if="notice" class="alert" role="status">{{ notice }}</p>

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
const { cancel, exportUrl } = useDebates()

// The id lives in the URL, so a reload recovers the view for as long as the server still
// holds the record (~30 minutes after completion).
const debateId = computed(() => String(route.params.id ?? ''))

/** Outcome of the last action the user took, when it succeeded but is worth spelling out. */
const notice = ref<string | null>(null)
const cancelling = ref(false)
/** A failure from the Cancel action, which must not be confused with a failure to load. */
const actionError = ref<ParsedApiError | null>(null)

/**
 * The record is a page-level read, so it goes through `useAsyncData`.
 *
 * On the container build the server now fetches it and the debate's title, status and verdict
 * are in the first HTML response; previously `onMounted` fetched it and the first paint was a
 * heading over an empty timeline. On the static export `useApiQuery` pins this to the client,
 * because `/debates/[id]` is not prerendered and its ids only exist at runtime anyway.
 *
 * The key is a getter, so navigating from one debate to another re-fetches instead of
 * leaving the previous record on screen.
 */
const { data: record, error: loadError, refresh: reloadRecord } =
  await useApiQuery<DebateResponse>(
    () => `debate:${debateId.value}`,
    () => `/debates/${encodeURIComponent(debateId.value)}`,
  )

const { rounds, errorMessage: streamError, connected, polling, start, close } = useDebateStream()

const exportHref = computed(() => exportUrl(debateId.value))

const visibleError = computed(() => actionError.value ?? loadError.value)

// The debate's own label is the most specific thing this page can be called, and it only
// arrives after the first render — so the title fills in rather than committing to the
// opaque id up front. Long labels are cut: a tab strip is not a place for a full question.
const TITLE_MAX = 60

useHead({
  title: computed(() => {
    const current = record.value
    const specific = current?.label?.trim() || current?.templateId?.trim()
    if (specific) {
      const shortened =
        specific.length > TITLE_MAX ? `${specific.slice(0, TITLE_MAX - 1)}…` : specific
      return `${shortened} — Delibera`
    }
    return `Debate ${debateId.value.slice(0, 8)} — Delibera`
  }),
})

/**
 * Backfills rounds from the paginated endpoint when the timeline is still empty.
 *
 * This has to run in BOTH cases, and that is the bug this shape fixes. A debate reached by
 * clicking through from the list arrives already Completed, so seeding is the only source of
 * rounds. One reached right after creation arrives Running: the stream is opened, and if the
 * debate finishes before the page settles, the terminal event carries no rounds — so the
 * timeline stayed empty while the verdict panel was already populated. Seeding unconditionally
 * covers both, and the stream dedupes by round number when it does deliver.
 */
async function seedRounds(): Promise<void> {
  try {
    const fetched = await useDebates().rounds(debateId.value)
    for (const round of fetched) {
      if (!rounds.value.some((existing) => existing.roundNumber === round.roundNumber)) {
        rounds.value.push(round)
      }
    }
    rounds.value.sort((a, b) => a.roundNumber - b.roundNumber)
  } catch {
    // Rounds are supplementary; the verdict panel above is the authoritative result.
  }
}

/**
 * Tears down the previous debate's stream, then opens one only if this debate can still change.
 *
 * `close()` runs unconditionally, and that ordering is the point. Navigating from a Running
 * debate to an already-Completed one must not leave the old EventSource attached: it would keep
 * pushing the previous debate's rounds into a timeline that now belongs to a different record.
 * `start()` also closes first, but it is not reached at all when the new debate is terminal.
 */
function syncStream(): void {
  close()
  if (!record.value || isActive(record.value.status)) {
    start(debateId.value, onStreamFinished)
  }
}

/**
 * Terminal SSE events carry no token stats, voting or duration, so the authoritative
 * record is re-fetched once the stream ends. This is also what populates the verdict panel.
 *
 * Rounds are backfilled here as well: a debate that completed before the timeline attached
 * gets its rounds from the endpoint rather than from the stream.
 */
async function onStreamFinished(): Promise<void> {
  await reloadRecord()
  if (rounds.value.length === 0) {
    await seedRounds()
  }
}

async function onCancel(): Promise<void> {
  cancelling.value = true
  notice.value = null
  actionError.value = null
  try {
    const outcome = await cancel(debateId.value)
    // Both outcomes mean "this debate will not run any longer", but they are not the same
    // event and the user pressed a button called Cancel: telling them a debate ended a
    // second earlier is different information from confirming their own cancellation, and
    // silently reloading the record conflates the two into an unexplained status change.
    notice.value =
      outcome === 'already-finished'
        ? 'This debate had already reached a final state, so there was nothing to cancel.'
        : 'Cancellation requested. The debate stops after the round in flight.'
    await reloadRecord()
  } catch (error) {
    actionError.value = error as ParsedApiError
  } finally {
    cancelling.value = false
  }
}

onMounted(async () => {
  syncStream()
  await seedRounds()
})

// Moving from one debate to another reuses this page component, so without this the previous
// debate's stream, rounds and record would all stay on screen under a new URL.
watch(debateId, async () => {
  notice.value = null
  actionError.value = null
  rounds.value = []
  syncStream()
  await seedRounds()
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
  color: var(--text-muted);
  flex-wrap: wrap;
}

.header__actions {
  display: flex;
  gap: 0.6rem;
}

.btn {
  background: var(--accent-btn-bg);
  color: var(--accent-btn-text);
  border: 1px solid var(--border-btn);
  border-radius: var(--radius-sm);
  padding: 0.4rem 0.8rem;
  font-size: 0.85rem;
  font-family: inherit;
  cursor: pointer;
  text-decoration: none;
}

.btn--danger {
  background: var(--danger-bg);
  border-color: var(--danger-border);
  color: var(--danger-fg);
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* See the note in app/pages/index.vue. */
.btn:hover:not(:disabled) {
  border-color: var(--border-btn-hover);
}

.btn--danger:hover:not(:disabled) {
  border-color: var(--danger-border-hover);
}

.btn:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.alert {
  padding: 0.8rem 1rem;
  border-radius: var(--radius);
  background: var(--surface);
  border: 1px solid var(--border);
  color: var(--text-soft);
  margin: 0 0 1rem;
}

.alert--error {
  border-color: var(--danger-border);
  background: var(--danger-bg);
  color: var(--danger-fg);
}

.alert__link {
  color: var(--accent-btn-text);
  margin-left: 0.5rem;
}

/* An id is an opaque token, not a word: fixed pitch stops the line from reflowing as it
   wraps, and it matches the mono used for the readouts further down. */
code {
  font-family: var(--font-mono);
  background: var(--code-bg);
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
  word-break: break-all;
}

/* Stream-state badges.
   These are declared here rather than reused from StatusBadge.vue because that component's
   styles are SCOPED to it — importing the class name would have produced an unstyled element
   and, with it, a malformed accessibility tree. */
.badge {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.7rem;
  font-weight: 600;
  padding: 0.12rem 0.5rem;
  border-radius: var(--radius-pill);
  border: 1px solid transparent;
  white-space: nowrap;
}

.badge--live {
  color: var(--live-fg);
  border-color: var(--live-border);
  background: var(--live-bg);
}

.badge--warn {
  color: var(--warn-fg);
  border-color: var(--warn-border);
  background: var(--warn-bg);
}

/* The one animation in the app. A debate runs for 150–200 seconds behind a badge that would
   otherwise sit still the entire time, so the dot is the only thing on the page telling the
   reader the stream is still open. `prefers-reduced-motion` neutralises it in main.css. */
.badge__pulse {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
  animation: live-pulse 1.8s ease-in-out infinite;
}

@keyframes live-pulse {
  0%,
  100% {
    opacity: 0.35;
    transform: scale(0.85);
  }
  50% {
    opacity: 1;
    transform: scale(1);
  }
}

@media (max-width: 640px) {
  .header {
    flex-direction: column;
    align-items: stretch;
  }

  .header__actions .btn {
    flex: 1 1 auto;
    text-align: center;
  }
}
</style>