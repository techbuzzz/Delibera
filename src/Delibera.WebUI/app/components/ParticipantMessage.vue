<template>
  <div class="message">
    <div class="message__header">
      <span class="message__role">{{ message.role }}</span>
      <!--
        `modelName` is always `string.Empty` server-side (DebateMapper.cs:94), so it is
        rendered only when non-empty — otherwise every bubble would carry a blank model tag.
      -->
      <span v-if="message.modelName" class="message__model">{{ message.modelName }}</span>
    </div>
    <p class="message__content">{{ message.content }}</p>
  </div>
</template>

<script setup lang="ts">
import type { ParticipantMessageDto } from '#shared/types/delibera'

defineProps<{ message: ParticipantMessageDto }>()
</script>

<style scoped>
/*
 * A speech, not a chat bubble. Who is speaking is a label in the margin rather than a rounded
 * container with a coloured edge: within a round several roles alternate, and once the boxes
 * start competing with each other the reader is scanning shapes instead of reading arguments.
 * The role label is mono because it names a machine role the way a transcript header would.
 */
.message {
  padding-left: 0.75rem;
  border-left: 1px solid var(--border-input);
}

.message__header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.3rem;
  flex-wrap: wrap;
}

.message__role {
  font-family: var(--font-mono);
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--live-fg);
}

.message__model {
  font-family: var(--font-mono);
  font-size: 0.68rem;
  color: var(--text-muted);
  background: var(--surface-3);
  padding: 0.05rem 0.4rem;
  border-radius: 4px;
}

/* This is the argument itself — the reason the reader came here — so it is set as prose in the
   serif face with a measure cap, rather than as UI text. */
.message__content {
  margin: 0;
  font-family: var(--font-serif);
  font-size: 0.95rem;
  line-height: 1.7;
  max-width: 68ch;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>