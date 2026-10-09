<template>
  <div class="error-page">
    <h1 class="code">{{ error?.statusCode ?? 500 }}</h1>
    <p class="message">{{ error?.message || 'Something went wrong.' }}</p>
    <NuxtLink to="/" class="btn">Back to debates</NuxtLink>
  </div>
</template>

<script setup lang="ts">
import type { NuxtError } from '#app'

defineProps<{ error: NuxtError }>()
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
  text-decoration: none;
}
</style>