<template>
  <section>
    <div class="toolbar">
      <h1 class="title">Debates</h1>

      <div class="filters">
        <select v-model="templateId" class="input" aria-label="Filter by template">
          <option value="">All templates</option>
          <option v-for="t in templates" :key="t.templateId" :value="t.templateId">
            {{ t.displayName }}
          </option>
        </select>

        <select v-model="status" class="input" aria-label="Filter by status">
          <option value="">All statuses</option>
          <option v-for="s in DEBATE_STATUSES" :key="s" :value="s">{{ describeStatus(s) }}</option>
        </select>

        <button class="btn" :disabled="loading" @click="refreshList">
          {{ loading ? 'Loading…' : 'Refresh' }}
        </button>
      </div>
    </div>

    <p v-if="error" class="alert alert--error">
      {{ error.message }}
      <span v-if="error.detail" class="alert__meta"> — {{ error.detail }}</span>
      <span v-if="error.correlationId" class="alert__meta"> correlation: {{ error.correlationId }}</span>
    </p>

    <p v-else-if="rows.length === 0 && !loading" class="alert">
      No debates yet. Create one from a registered template to get started.
    </p>

    <ul v-else class="list" :aria-busy="loading">
      <li v-for="debate in rows" :key="debate.debateId">
        <NuxtLink :to="`/debates/${debate.debateId}`" class="row">
          <div class="row__main">
            <span class="row__template">{{ debate.templateId }}</span>
            <span class="row__label">{{ debate.label || debate.debateId.slice(0, 8) }}</span>
          </div>

          <StatusBadge :status="debate.status" />

          <div class="row__meta">
            <span v-if="debate.tokenStats">
              {{ debate.tokenStats.totalTokens.toLocaleString() }} tokens
            </span>
            <span v-if="debate.durationMs != null">
              {{ (debate.durationMs / 1000).toFixed(1) }}s
            </span>
            <time :datetime="debate.createdAt">{{ formatTime(debate.createdAt) }}</time>
          </div>
        </NuxtLink>
      </li>
    </ul>

    <nav v-if="page > 1 || hasNextPage" class="pager">
      <button class="btn" :disabled="page <= 1 || loading" @click="goTo(page - 1)">Previous</button>
      <span class="pager__label">Page {{ page }}</span>
      <button class="btn" :disabled="!hasNextPage || loading" @click="goTo(page + 1)">Next</button>
    </nav>
  </section>
</template>

<script setup lang="ts">
import type { DebateResponse, TemplateDto } from '#shared/types/delibera'
import { DEBATE_STATUSES } from '#shared/types/delibera'
import { describeStatus } from '~/composables/useApiError'

// Without this the tab reads "Delibera — AI council" on every screen, which identifies the
// product but not the page — and with three routes open there is nothing to tell them apart.
useHead({ title: 'Debates — Delibera' })

const templateId = ref('')
const status = ref('')
const page = ref(1)

const PAGE_SIZE = 20

/**
 * The list is one reactive key, not a watcher plus a reload function.
 *
 * Filters and page are inputs to the same question ("which debates match these criteria?"), so
 * they belong in the query key: change any of them and Nuxt re-runs the handler by itself.
 * The previous shape drove the fetch from `onMounted` plus a `watch`, which meant the controls
 * and the list below them could briefly disagree about what was being shown.
 *
 * `server: false` — and the reason is easy to get wrong. `nitro.prerender.routes` includes '/'
 * in nuxt.config.ts, and that list is honoured by EVERY `nuxt build`, not only by the GitHub
 * Pages export: Nitro prerenders those routes at build time and then serves the resulting static
 * HTML in preference to rendering them. A server-side read here would run once, on the build
 * machine, and its result would be baked into `index.html` for every visitor — a debate list
 * frozen at deploy time, which no client refetch would replace because a cached success is
 * precisely what `getCachedData` reuses. The client fetch below is what keeps this page honest,
 * and the reactive key is what keeps the controls honest about it.
 */
const { data: debates, error, pending: loading, refresh: refetch } =
  await useApiQuery<DebateResponse[]>(
    () => `debates:${templateId.value}:${status.value}:${page.value}`,
    () => {
      const query = new URLSearchParams()
      if (templateId.value) query.set('templateId', templateId.value)
      if (status.value) query.set('status', status.value)
      query.set('page', String(page.value))
      query.set('pageSize', String(PAGE_SIZE))
      return `/debates?${query.toString()}`
    },
    { server: false },
  )

/**
 * `GET /debates` returns a bare array with no total count, so "is there a next page" has to be
 * inferred: a full page is the only evidence available. Fetching one extra row and trimming
 * would report the same thing, so the simple test is used and the last page may be exactly full.
 */
const hasNextPage = computed(() => rows.value.length === PAGE_SIZE)

/**
 * Templates stay client-fetched even on the container build.
 *
 * They are tenant-scoped configuration rather than a page's own content, and `/debates/new` is
 * one of the two prerendered routes. Rendering them into the static HTML would bake one
 * tenant's registry into a file published for everyone, so this is the one read in the app
 * that is deliberately never server-run.
 */
const { data: templates } = await useApiQuery<TemplateDto[]>('templates', '/templates', {
  server: false,
})

/** Empty while loading or after a failure, so the list template never iterates over a null. */
const rows = computed(() => debates.value ?? [])

/**
 * Changing a filter resets paging.
 *
 * Page 3 of a filter the user has just replaced is a page that has no meaning under the new
 * selection. The reset itself changes the query key, so no explicit reload is needed.
 */
watch([templateId, status], () => {
  page.value = 1
})

/**
 * Manual refresh: back to the first page of whatever is currently selected.
 *
 * Changing the page already re-runs the handler through the reactive key, so forcing a
 * `refresh()` on top of that would issue a second, redundant request whose two responses race
 * to write the same list. Return early and let the key change do the work.
 */
async function refreshList(): Promise<void> {
  if (page.value !== 1) {
    page.value = 1
    return
  }
  await refetch()
}

function goTo(next: number): void {
  page.value = next
}

function formatTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
</script>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 1.25rem;
}

.title {
  margin: 0;
  font-size: 1.35rem;
}

.filters {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.input {
  background: var(--surface);
  color: var(--text);
  border: 1px solid var(--border-input);
  border-radius: var(--radius-sm);
  padding: 0.4rem 0.55rem;
  font-size: 0.85rem;
  font-family: inherit;
}

.btn {
  background: var(--accent-btn-bg);
  color: var(--accent-btn-text);
  border: 1px solid var(--border-btn);
  border-radius: var(--radius-sm);
  padding: 0.4rem 0.8rem;
  font-size: 0.85rem;
  font-family: inherit;
  cursor: pointer;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Hover and keyboard focus.
   `:focus-visible` rather than `:focus`, so a pointer click does not leave a ring behind on
   every row while keyboard navigation still shows exactly where the reader is. The app has
   no :focus styling at all elsewhere, and the browser default is a thin outline that all but
   disappears against these dark surfaces. */
.btn:hover:not(:disabled) {
  border-color: var(--border-btn-hover);
}

.input:hover {
  border-color: var(--border-input-hover);
}

.input:focus-visible,
.btn:focus-visible,
.row:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.row {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 1rem;
  align-items: center;
  padding: 0.8rem 1rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  text-decoration: none;
  color: inherit;
}

.row:hover {
  border-color: var(--border-input-hover);
}

.row__main {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}

.row__template {
  font-weight: 600;
  font-size: 0.95rem;
}

/* The debate's own label is the argument being put to the council, so it is set as prose
   rather than as a second line of chrome — and trimmed to one line so a long question cannot
   stretch a row into a wall of text. */
.row__label {
  font-family: var(--font-serif);
  font-size: 0.85rem;
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.row__meta {
  display: flex;
  gap: 0.75rem;
  font-family: var(--font-mono);
  font-size: 0.72rem;
  color: var(--text-muted);
  /* Token counts and durations are read against each other down the column; a fixed pitch is
     what makes two-digit and six-digit runs align. */
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.alert {
  padding: 0.8rem 1rem;
  border-radius: var(--radius);
  background: var(--surface);
  border: 1px solid var(--border);
  color: var(--text-soft);
}

.alert--error {
  border-color: var(--danger-border);
  background: var(--danger-bg);
  color: var(--danger-fg);
}

.alert__meta {
  color: var(--text-muted);
  font-size: 0.75rem;
  font-family: var(--font-mono);
}

.pager {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  margin-top: 1.25rem;
}

.pager__label {
  font-size: 0.8rem;
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

/* ── Narrow screens ─────────────────────────────────────────────────────────
   The row is a three-column grid whose narrowest two are fixed-size metadata. Below ~640px
   that leaves the debate title with almost no width while the timestamp keeps all of its,
   so the columns stack and the metadata wraps instead of compressing. */
@media (max-width: 640px) {
  .filters {
    width: 100%;
  }

  .filters .input {
    flex: 1 1 8rem;
    min-width: 0;
  }

  .row {
    grid-template-columns: 1fr;
    gap: 0.5rem;
    /* Without this the stacked grid stretches its items to the full column width, and the
       status badge — an inline pill — renders as a full-width bar. */
    justify-items: start;
  }

  .row__meta {
    flex-wrap: wrap;
    gap: 0.5rem 0.75rem;
    white-space: normal;
  }
}
</style>