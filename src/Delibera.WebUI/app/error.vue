<template>
  <div class="error-page">
    <h1 class="code">{{ error?.statusCode ?? 500 }}</h1>
    <!-- statusMessage is the reason phrase ("Not Found"), which is more specific than the
         generic message Vue Router carries over; either beats a bare "Something went wrong". -->
    <p class="message">{{ error?.statusMessage || error?.message || 'Something went wrong.' }}</p>
    <button type="button" class="btn" @click="goHome">Back to debates</button>
  </div>
</template>

<script setup lang="ts">
import type { NuxtError } from '#app'

defineProps<{ error: NuxtError }>()

/**
 * Clears the error before navigating.
 *
 * Nuxt renders this page INSTEAD of the app, so the router state a link would need is not
 * there — which is why the escape hatch is an explicit button rather than a NuxtLink that
 * would only appear to work.
 */
function goHome(): void {
  void clearError({ redirect: '/' })
}
</script>

<style scoped>
.error-page {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  padding: 3rem 1rem;
  text-align: center;
}

.code {
  margin: 0;
  font-size: 2.5rem;
  color: #ff9a9a;
}

.message {
  margin: 0;
  color: #9fb0d0;
}

.btn {
  margin-top: 0.5rem;
  background: #24314a;
  color: #dce6ff;
  border: 1px solid #33415c;
  border-radius: 6px;
  padding: 0.45rem 0.9rem;
  font-size: 0.85rem;
  cursor: pointer;
}

.btn:hover {
  border-color: #4a5a7a;
}

.btn:focus-visible {
  outline: 2px solid #7fb0ff;
  outline-offset: 2px;
}
</style>