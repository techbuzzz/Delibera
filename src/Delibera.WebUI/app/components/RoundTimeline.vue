<template>
  <div class="timeline">
    <h2 class="timeline__title">Rounds ({{ rounds.length }})</h2>

    <!--
      The rounds arrive over SSE over a couple of hundred seconds, so the timeline changes
      while the reader is somewhere else entirely — most likely on the verdict panel above.
      A visible count is not something a screen reader re-announces on its own; this is.

      Polite, not assertive: rounds arrive as a steady drip, and interrupting whatever is
      being read for each one would make the page unusable rather than live. One message per
      round is the right cadence.
    -->
    <p class="sr-only" role="status" aria-live="polite">{{ liveMessage }}</p>

    <p v-if="rounds.length === 0" class="empty">
      No rounds yet. A debate typically takes 150–200 seconds to complete.
    </p>

    <!--
      An ordered list, not a stack of articles. Rounds are a sequence and the number carries
      meaning — a reader who lands mid-debate counts the marker down the rule to see where
      they are, which a set of visually identical cards cannot express. The <ol> also gives
      assistive technology the count for free.
    -->
    <ol v-else class="proceedings">
      <li v-for="round in rounds" :key="round.roundNumber" class="round">
        <div class="round__marker" aria-hidden="true">
          <span class="round__number">{{ round.roundNumber }}</span>
        </div>

        <div class="round__body">
          <header class="round__header">
            <h3 class="round__heading">Round {{ round.roundNumber }}</h3>
            <!--
              `strategy` is a CLR type name such as `StandardDebateStrategy`, or an empty
              string (DebateMapper.cs:84). Raw display is intentional — prettifying it would
              invent a name the server never sent.
            -->
            <span v-if="round.strategy" class="round__strategy">{{ round.strategy }}</span>
            <span v-if="round.isFinal" class="round__final">final</span>
          </header>

          <div v-if="round.messages?.length" class="messages">
            <ParticipantMessage
              v-for="(message, index) in round.messages"
              :key="`${round.roundNumber}-${index}`"
              :message="message"
            />
          </div>
          <p v-else class="empty">No participant messages in this round.</p>

          <!-- The chairman's summary is the minute of the round, so it is set apart from the
               speeches it condenses rather than presented as another participant. -->
          <section v-if="round.chairmanSummary" class="minutes">
            <span class="minutes__label">Chairman</span>
            <p class="minutes__text">{{ round.chairmanSummary }}</p>
          </section>

          <details v-if="round.operatorInteractions?.length" class="operator">
            <summary>Operator interactions ({{ round.operatorInteractions.length }})</summary>
            <div v-for="(item, index) in round.operatorInteractions" :key="index" class="operator__item">
              <span class="operator__task">{{ item.task }}</span>
              <pre class="operator__result">{{ item.result }}</pre>
            </div>
          </details>
        </div>
      </li>
    </ol>
  </div>
</template>

<script setup lang="ts">
import type { DebateRoundDto } from '#shared/types/delibera'

const props = defineProps<{ rounds: DebateRoundDto[] }>()

/**
 * What the live region says. Empty before the first round so the region is silent rather
 * than announcing a zero it has no news about.
 */
const liveMessage = computed(() => {
  const count = props.rounds.length
  if (count === 0) return ''
  const latest = props.rounds[count - 1]?.roundNumber ?? count
  return `Round ${latest} received, ${count} in total.`
})
</script>

<style scoped>
/* Available to assistive technology, absent from the visual layout. */
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: -1px;
  padding: 0;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
  border: 0;
}

.timeline {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.timeline__title {
  margin: 0;
  font-size: 1rem;
  color: var(--text-soft);
}

.empty {
  color: var(--text-muted);
  font-size: 0.85rem;
  margin: 0.25rem 0;
}

/* The rule the rounds hang from. It is the whole structure of this view: one continuous line
   down the left, with each round marked on it, so the passage of a live debate reads as
   progress along a line rather than as an accumulating pile. */
.proceedings {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  position: relative;
}

.proceedings::before {
  content: '';
  position: absolute;
  left: 13px;
  top: 0.5rem;
  bottom: 0.5rem;
  width: 1px;
  background: var(--border);
}

.round {
  display: grid;
  grid-template-columns: 28px 1fr;
  gap: 0.9rem;
  position: relative;
}

/* Sits on the rule rather than beside it, so consecutive markers form a single sequence. */
.round__marker {
  display: flex;
  justify-content: center;
}

.round__number {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 27px;
  height: 27px;
  border-radius: 50%;
  background: var(--surface-2);
  border: 1px solid var(--border-input);
  color: var(--text-soft);
  font-family: var(--font-mono);
  font-size: 0.72rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.round__body {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 0.9rem 1rem;
  min-width: 0;
}

.round__header {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  margin-bottom: 0.7rem;
  flex-wrap: wrap;
}

.round__heading {
  margin: 0;
  font-size: 0.9rem;
  font-weight: 600;
}

.round__strategy {
  font-size: 0.68rem;
  color: var(--text-muted);
  background: var(--surface-2);
  padding: 0.1rem 0.45rem;
  border-radius: 4px;
  font-family: var(--font-mono);
}

.round__final {
  font-size: 0.68rem;
  color: var(--ok-fg);
  border: 1px solid var(--ok-border);
  padding: 0.05rem 0.4rem;
  border-radius: var(--radius-pill);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.messages {
  display: flex;
  flex-direction: column;
  gap: 0.7rem;
}

.minutes {
  margin-top: 0.85rem;
  padding: 0.7rem 0.85rem;
  background: var(--surface-2);
  border-left: 2px solid var(--accent-strong);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
}

.minutes__label {
  display: block;
  font-size: 0.68rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-muted);
  font-family: var(--font-mono);
}

/* The summary is prose written for the reader, not a status line — hence the serif and the
   measure cap, so a chairman writing at length does not produce one 1100px-wide line. */
.minutes__text {
  margin: 0.35rem 0 0;
  font-family: var(--font-serif);
  font-size: 0.95rem;
  line-height: 1.65;
  max-width: 68ch;
  white-space: pre-wrap;
}

.operator {
  margin-top: 0.7rem;
}

.operator summary {
  cursor: pointer;
  font-size: 0.78rem;
  color: var(--text-soft);
}

.operator__item {
  margin-top: 0.5rem;
}

.operator__task {
  font-size: 0.72rem;
  color: var(--text-muted);
}

.operator__result {
  margin: 0.2rem 0 0;
  padding: 0.5rem;
  background: var(--surface-3);
  border-radius: var(--radius-sm);
  font-family: var(--font-mono);
  font-size: 0.75rem;
  overflow-x: auto;
}

/* ── Narrow screens ─────────────────────────────────────────────────────────
   The 28px marker column is worth keeping on a phone — it is what makes the sequence
   readable — but the speaker layout inside a round stops being a two-column margin. */
@media (max-width: 640px) {
  .proceedings::before {
    left: 10px;
  }

  .round {
    grid-template-columns: 21px 1fr;
    gap: 0.6rem;
  }

  .round__number {
    width: 21px;
    height: 21px;
    font-size: 0.65rem;
  }

  .round__body {
    padding: 0.75rem 0.8rem;
  }
}
</style>