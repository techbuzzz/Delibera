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
.message {
  background: #1b202b;
  border-radius: 6px;
  padding: 0.6rem 0.75rem;
}

.message__header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.35rem;
}

.message__role {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #9fd0ff;
}

.message__model {
  font-size: 0.68rem;
  color: #7d879c;
  background: #0d1016;
  padding: 0.05rem 0.4rem;
  border-radius: 4px;
}

.message__content {
  margin: 0;
  font-size: 0.88rem;
  line-height: 1.65;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>