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

        <button class="btn" :disabled="loading" @click="refresh">
          {{ loading ? 'Loading…' : 'Refresh' }}
        </button>
      </div>
    </div>

    <p v-if="error" class="alert alert--error">
      {{ error.message }}
      <span v-if="error.correlationId" class="alert__meta"> correlation: {{ error.correlationId }}</span>
    </p>

    <p v-else-if="debates.length === 0 && !loading" class="alert">
      No debates yet. Create one from a registered template to get started.
    </p>

    <ul v-else class="list">
      <li v-for="debate in debates" :key="debate.debateId">
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
import { describeStatus, type ParsedApiError } from '~/composables/useApiError'

const { list, templates: fetchTemplates } = useDebates()

const debates = ref<DebateResponse[]>([])
const templates = ref<TemplateDto[]>([])
const loading = ref(false)
const error = ref<ParsedApiError | null>(null)

const templateId = ref('')
const status = ref('')
const page = ref(1)

const PAGE_SIZE = 20
// GET /debates returns a bare array with no total count, so "is there a next page" has to be
// inferred: a full page is the only evidence available. Fetching one extra row and trimming
// would report the same thing, so the simple test is used and the last page may be exactly full.
const hasNextPage = computed(() => debates.value.length === PAGE_SIZE)

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    debates.value = await list({
      templateId: templateId.value || undefined,
      status: status.value || undefined,
      page: page.value,
      pageSize: PAGE_SIZE,
    })
  } catch (e) {
    error.value = e as ParsedApiError
    debates.value = []
  } finally {
    loading.value = false
  }
}

async function refresh(): Promise<void> {
  page.value = 1
  await load()
}

function goTo(next: number): void {
  page.value = next
  void load()
}

function formatTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

onMounted(async () => {
  await load()
  try {
    // GET /templates is the source of truth for valid template ids; the validator rejects
    // anything unregistered, so hardcoding the list would only guarantee drift.
    templates.value = await fetchTemplates()
  } catch {
    // The filter degrades to "All templates"; the list itself still works.
    templates.value = []
  }
})
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
}

.input {
  background: #151922;
  color: #e6e6e6;
  border: 1px solid #2b3242;
  border-radius: 6px;
  padding: 0.4rem 0.55rem;
  font-size: 0.85rem;
}

.btn {
  background: #24314a;
  color: #dce6ff;
  border: 1px solid #33415c;
  border-radius: 6px;
  padding: 0.4rem 0.8rem;
  font-size: 0.85rem;
  cursor: pointer;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
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
  background: #151922;
  border: 1px solid #262b36;
  border-radius: 8px;
  text-decoration: none;
  color: inherit;
}

.row:hover {
  border-color: #3a445c;
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

.row__label {
  font-size: 0.78rem;
  color: #7d879c;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.row__meta {
  display: flex;
  gap: 0.75rem;
  font-size: 0.75rem;
  color: #7d879c;
}

.alert {
  padding: 0.8rem 1rem;
  border-radius: 8px;
  background: #151922;
  border: 1px solid #262b36;
  color: #9fb0d0;
}

.alert--error {
  border-color: #6b2c2c;
  background: #241212;
  color: #ff9a9a;
}

.alert__meta {
  color: #7d879c;
  font-size: 0.75rem;
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
  color: #7d879c;
}
</style>