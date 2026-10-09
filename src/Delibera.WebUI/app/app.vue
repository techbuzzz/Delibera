<template>
  <div class="app">
    <header class="app__header">
      <NuxtLink to="/" class="app__brand">
        <span class="app__brand-mark">Delibera</span>
        <span class="app__brand-sub">AI council · web UI</span>
      </NuxtLink>

      <nav class="app__nav">
        <NuxtLink to="/">Debates</NuxtLink>
        <NuxtLink to="/debates/new">New debate</NuxtLink>
      </nav>

      <ClientOnly>
        <span class="app__health" :class="healthClass" :title="healthTitle">
          {{ healthLabel }}
        </span>
      </ClientOnly>
    </header>

    <main class="app__main">
      <NuxtPage />
    </main>
  </div>
</template>

<script setup lang="ts">
/**
 * Health badge.
 *
 * ClientOnly because the probe is a live upstream call — running it during SSR would make
 * every server-rendered page wait on the API, turning a slow backend into a slow UI.
 */
const ok = ref(false)
const degraded = ref(false)

onMounted(async () => {
  try {
    const response = await fetch('/api/health')
    const body = await response.json()
    ok.value = !!body.ok
    degraded.value = !!body.degraded
  } catch {
    ok.value = false
    degraded.value = true
  }
})

const healthLabel = computed(() =>
  ok.value && !degraded.value ? 'Healthy' : degraded.value ? 'Degraded' : 'Offline',
)
const healthClass = computed(() =>
  ok.value && !degraded.value ? 'is-ok' : degraded.value ? 'is-degraded' : 'is-down',
)
const healthTitle = computed(() =>
  ok.value && !degraded.value
    ? 'Delibera API reachable'
    : 'Delibera API unreachable or degraded',
)
</script>

<style scoped>
.app {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: #0f1115;
  color: #e6e6e6;
  font-family: ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif;
}

.app__header {
  display: flex;
  align-items: center;
  gap: 1.5rem;
  padding: 0.85rem 1.5rem;
  border-bottom: 1px solid #262b36;
  background: #151922;
}

.app__brand {
  display: flex;
  flex-direction: column;
  text-decoration: none;
  color: inherit;
  line-height: 1.15;
}

.app__brand-mark {
  font-weight: 700;
  letter-spacing: 0.02em;
}

.app__brand-sub {
  font-size: 0.72rem;
  color: #7d879c;
}

.app__nav {
  display: flex;
  gap: 1rem;
  margin-left: auto;
}

.app__nav a {
  color: #9fb0d0;
  text-decoration: none;
  font-size: 0.9rem;
  padding: 0.35rem 0.6rem;
  border-radius: 6px;
}

.app__nav a:hover,
.app__nav a.router-link-active {
  background: #222836;
  color: #fff;
}

.app__health {
  font-size: 0.72rem;
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
  border: 1px solid transparent;
  white-space: nowrap;
}

.is-ok {
  color: #7ee2a8;
  border-color: #2c5c40;
  background: #12241a;
}

.is-degraded {
  color: #ffd479;
  border-color: #6b551f;
  background: #241d0f;
}

.is-down {
  color: #ff9a9a;
  border-color: #6b2c2c;
  background: #241212;
}

.app__main {
  flex: 1;
  padding: 1.5rem;
  max-width: 1100px;
  width: 100%;
  margin: 0 auto;
}
</style>