<template>
  <div class="app">
    <!--
      The skip link is the first focusable element on the page, so a keyboard user crosses the
      header without meeting the navigation. It is hidden until focused rather than
      `display: none`, which would remove it from the tab order entirely and defeat the point.
    -->
    <a class="skip-link" href="#main">Skip to content</a>

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

    <!-- `tabindex="-1"` so the skip link can actually move focus here. Without it the link
         scrolls the page but leaves the next Tab press back at the top of the header. -->
    <main id="main" class="app__main" tabindex="-1">
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
  background: var(--bg);
  color: var(--text);
  font-family: var(--font-sans);
}

.skip-link {
  position: absolute;
  left: 0.5rem;
  top: -3rem;
  z-index: 10;
  padding: 0.5rem 0.85rem;
  background: var(--accent-primary);
  color: var(--text-strong);
  border-radius: var(--radius-sm);
  font-size: 0.85rem;
  text-decoration: none;
  /* Moves into view rather than appearing, so focus is not lost to an abrupt layout change. */
  transition: top 0.15s ease;
}

.skip-link:focus {
  top: 0.5rem;
}

.app__header {
  display: flex;
  align-items: center;
  gap: 1.5rem;
  padding: 0.85rem 1.5rem;
  border-bottom: 1px solid var(--border);
  background: var(--surface);
  flex-wrap: wrap;
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
  color: var(--text-muted);
}

.app__nav {
  display: flex;
  gap: 1rem;
  margin-left: auto;
}

.app__nav a {
  color: var(--text-soft);
  text-decoration: none;
  font-size: 0.9rem;
  padding: 0.35rem 0.6rem;
  border-radius: var(--radius-sm);
}

.app__nav a:hover,
.app__nav a.router-link-active {
  background: var(--surface-nav);
  color: var(--text-strong);
}

/* The nav is the only always-visible route control in the app, so a keyboard user crossing
   the page meets it first. The browser default outline is near-invisible on --surface. */
.app__nav a:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.app__health {
  font-size: 0.72rem;
  padding: 0.2rem 0.55rem;
  border-radius: var(--radius-pill);
  border: 1px solid transparent;
  white-space: nowrap;
}

.is-ok {
  color: var(--ok-fg);
  border-color: var(--ok-border);
  background: var(--ok-bg);
}

.is-degraded {
  color: var(--warn-fg);
  border-color: var(--warn-border);
  background: var(--warn-bg);
}

.is-down {
  color: var(--danger-fg);
  border-color: var(--danger-border);
  background: var(--danger-bg);
}

.app__main {
  flex: 1;
  padding: 1.5rem;
  max-width: 1100px;
  width: 100%;
  margin: 0 auto;
}

/* Focusing <main> via the skip link should not draw a ring around the entire content area —
   the skip link itself is the visible indication that focus moved. */
.app__main:focus {
  outline: none;
}

@media (max-width: 640px) {
  .app__header {
    padding: 0.75rem 1rem;
    gap: 0.75rem;
  }

  /* Without the wrap, the brand, the nav and the health badge cannot share a phone-width row
     and the badge is pushed off the edge entirely. */
  .app__nav {
    margin-left: 0;
    order: 3;
    width: 100%;
  }

  .app__health {
    margin-left: auto;
  }

  .app__main {
    padding: 1rem;
  }
}
</style>