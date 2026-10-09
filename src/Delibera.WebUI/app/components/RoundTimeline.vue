<template>
  <div class="timeline">
    <h2 class="timeline__title">Rounds ({{ rounds.length }})</h2>

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

defineProps<{ rounds: DebateRoundDto[] }>()
</script>

<style scoped>
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