<template>
  <span class="badge" :class="`badge--${tone}`">{{ describeStatus(status) }}</span>
</template>

<script setup lang="ts">
import { describeStatus } from '~/composables/useApiError'

const props = defineProps<{ status: string }>()

const tone = computed(() => {
  switch (props.status) {
    case 'Completed':
      return 'ok'
    case 'Failed':
      return 'error'
    case 'Cancelled':
      return 'muted'
    case 'Running':
      return 'live'
    default:
      return 'pending'
  }
})
</script>

<style scoped>
.badge {
  display: inline-block;
  font-size: 0.7rem;
  font-weight: 600;
  padding: 0.12rem 0.5rem;
  border-radius: var(--radius-pill);
  border: 1px solid transparent;
  white-space: nowrap;
}

.badge--ok {
  color: var(--ok-fg);
  border-color: var(--ok-border);
  background: var(--ok-bg);
}

.badge--live {
  color: var(--live-fg);
  border-color: var(--live-border);
  background: var(--live-bg);
}

.badge--pending {
  color: var(--warn-fg);
  border-color: var(--warn-border);
  background: var(--warn-bg);
}

.badge--error {
  color: var(--danger-fg);
  border-color: var(--danger-border);
  background: var(--danger-bg);
}

.badge--muted {
  color: var(--neutral-fg);
  border-color: var(--border-muted);
  background: var(--surface-muted);
}
</style>