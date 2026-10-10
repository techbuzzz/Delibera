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

    <article v-for="round in rounds" :key="round.roundNumber" class="round">
      <header class="round__header">
        <span class="round__number">Round {{ round.roundNumber }}</span>
        <!--
          `strategy` is a CLR type name such as `StandardDebateStrategy`, or an empty string
          (DebateMapper.cs:84). Raw display is intentional — prettifying it would invent a
          name the server never sent.
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

      <div v-if="round.chairmanSummary" class="chairman">
        <span class="chairman__label">Chairman</span>
        <p class="chairman__text">{{ round.chairmanSummary }}</p>
      </div>

      <details v-if="round.operatorInteractions?.length" class="operator">
        <summary>Operator interactions ({{ round.operatorInteractions.length }})</summary>
        <div v-for="(item, index) in round.operatorInteractions" :key="index" class="operator__item">
          <span class="operator__task">{{ item.task }}</span>
          <pre class="operator__result">{{ item.result }}</pre>
        </div>
      </details>
    </article>
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
  color: #9fb0d0;
}

.empty {
  color: #7d879c;
  font-size: 0.85rem;
  margin: 0.25rem 0;
}

.round {
  background: #151922;
  border: 1px solid #262b36;
  border-radius: 8px;
  padding: 0.9rem 1rem;
}

.round__header {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  margin-bottom: 0.7rem;
}

.round__number {
  font-weight: 600;
  font-size: 0.9rem;
}

.round__strategy {
  font-size: 0.68rem;
  color: #7d879c;
  background: #1b202b;
  padding: 0.1rem 0.45rem;
  border-radius: 4px;
  font-family: ui-monospace, monospace;
}

.round__final {
  font-size: 0.68rem;
  color: #7ee2a8;
  border: 1px solid #2c5c40;
  padding: 0.05rem 0.4rem;
  border-radius: 999px;
}

.messages {
  display: flex;
  flex-direction: column;
  gap: 0.7rem;
}

.chairman {
  margin-top: 0.8rem;
  padding: 0.65rem 0.8rem;
  background: #1b202b;
  border-left: 2px solid #3d74cc;
  border-radius: 0 6px 6px 0;
}

.chairman__label {
  font-size: 0.68rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #7d879c;
}

.chairman__text {
  margin: 0.3rem 0 0;
  font-size: 0.88rem;
  line-height: 1.6;
  white-space: pre-wrap;
}

.operator {
  margin-top: 0.7rem;
}

.operator summary {
  cursor: pointer;
  font-size: 0.78rem;
  color: #9fb0d0;
}

.operator__item {
  margin-top: 0.5rem;
}

.operator__task {
  font-size: 0.72rem;
  color: #7d879c;
}

.operator__result {
  margin: 0.2rem 0 0;
  padding: 0.5rem;
  background: #0d1016;
  border-radius: 6px;
  font-size: 0.75rem;
  overflow-x: auto;
}
</style>