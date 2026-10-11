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
/* This page replaces the app entirely, so it cannot inherit the `.app` background or font —
   it sets its own. Without that it renders on the browser's default white, which is a visible
   regression for any error reached outside an in-app navigation. */
.error-page {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding: 3rem 1rem;
  text-align: center;
  background: var(--bg);
  color: var(--text);
  font-family: var(--font-sans);
}

.code {
  margin: 0;
  font-family: var(--font-mono);
  font-size: 2.5rem;
  color: var(--danger-fg);
  font-variant-numeric: tabular-nums;
}

.message {
  margin: 0;
  color: var(--text-soft);
}

.btn {
  margin-top: 0.5rem;
  background: var(--accent-btn-bg);
  color: var(--accent-btn-text);
  border: 1px solid var(--border-btn);
  border-radius: var(--radius-sm);
  padding: 0.45rem 0.9rem;
  font-size: 0.85rem;
  font-family: inherit;
  cursor: pointer;
}

.btn:hover {
  border-color: var(--border-btn-hover);
}

.btn:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}
</style>