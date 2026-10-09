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
  border-radius: 999px;
  border: 1px solid transparent;
  white-space: nowrap;
}

.badge--ok {
  color: #7ee2a8;
  border-color: #2c5c40;
  background: #12241a;
}

.badge--live {
  color: #9fd0ff;
  border-color: #2b4a6b;
  background: #12202e;
}

.badge--pending {
  color: #ffd479;
  border-color: #6b551f;
  background: #241d0f;
}

.badge--error {
  color: #ff9a9a;
  border-color: #6b2c2c;
  background: #241212;
}

.badge--muted {
  color: #9aa3b5;
  border-color: #333a48;
  background: #1b1f28;
}
</style>