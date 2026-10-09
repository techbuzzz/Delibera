<template>
  <section>
    <h1 class="title">New debate</h1>

    <p v-if="templates.length === 0" class="alert">
      No council templates are registered on the server, so a debate cannot be created.
      Check that <code>Delibera.Server</code> started with its template registry populated.
    </p>

    <form v-else class="form" @submit.prevent="submit">
      <label class="field">
        <span class="field__label">Template</span>
        <select v-model="form.templateId" class="input" required>
          <option value="" disabled>Select a template…</option>
          <option v-for="t in templates" :key="t.templateId" :value="t.templateId">
            {{ t.displayName }}
          </option>
        </select>
        <span v-if="selected" class="field__hint">
          {{ selected.strategy }} · up to {{ selected.defaultMaxRounds }} rounds ·
          roles: {{ selected.memberRoles.join(', ') }}
          <template v-if="selected.ragEnabled"> · RAG enabled</template>
        </span>
      </label>

      <label class="field">
        <span class="field__label">Question</span>
        <textarea
          v-model="form.question"
          class="input input--area"
          rows="5"
          :maxlength="LIMITS.questionMax"
          required
        />
        <!-- Live counter: the server enforces 1-4096 and rejects with a 400 otherwise, so
             showing the limit here saves a round trip on an otherwise-obvious error. -->
        <span class="field__hint" :class="{ 'is-over': form.question.length > LIMITS.questionMax }">
          {{ form.question.length }} / {{ LIMITS.questionMax }}
        </span>
      </label>

      <div class="field-row">
        <label class="field">
          <span class="field__label">Max rounds</span>
          <input
            v-model.number="form.maxRounds"
            type="number"
            class="input"
            :min="LIMITS.maxRoundsMin"
            :max="LIMITS.maxRoundsMax"
          />
        </label>

        <label class="field">
          <span class="field__label">Temperature</span>
          <input
            v-model.number="form.temperature"
            type="number"
            step="0.1"
            class="input"
            :min="LIMITS.temperatureMin"
            :max="LIMITS.temperatureMax"
          />
        </label>
      </div>

      <p v-if="clientError" class="alert alert--error">{{ clientError }}</p>

      <ul v-if="Object.keys(serverErrors).length" class="alert alert--error">
        <li v-for="(messages, field) in serverErrors" :key="field">
          <strong>{{ field }}</strong>: {{ messages.join(' ') }}
        </li>
      </ul>

      <p v-if="apiError" class="alert alert--error">
        {{ apiError.message }}
        <span v-if="apiError.detail" class="alert__meta"> — {{ apiError.detail }}</span>
        <span v-if="apiError.correlationId" class="alert__meta">
          correlation: {{ apiError.correlationId }}
        </span>
      </p>

      <div class="actions">
        <button type="submit" class="btn btn--primary" :disabled="submitting">
          {{ submitting ? 'Starting…' : 'Start debate' }}
        </button>
        <NuxtLink to="/" class="btn">Cancel</NuxtLink>
      </div>
    </form>
  </section>
</template>

<script setup lang="ts">
import type { CreateDebateRequest, TemplateDto } from '#shared/types/delibera'
import { LIMITS } from '#shared/types/delibera'
import type { ParsedApiError } from '~/composables/useApiError'

const { create, templates: fetchTemplates } = useDebates()
const router = useRouter()

const templates = ref<TemplateDto[]>([])
const submitting = ref(false)
const clientError = ref<string | null>(null)
const apiError = ref<ParsedApiError | null>(null)
const serverErrors = ref<Record<string, string[]>>({})

const form = reactive({
  templateId: '',
  question: '',
  maxRounds: 4,
  temperature: 0.7,
})

const selected = computed(() =>
  templates.value.find((t) => t.templateId === form.templateId),
)

// Pre-fill the round count from the chosen template so the UI does not contradict the
// template's own default.
watch(selected, (template) => {
  if (template) form.maxRounds = template.defaultMaxRounds
})

/**
 * Mirrors the server-side validator (`CreateDebateRequestValidator.cs`). The server stays
 * authoritative — this only avoids a guaranteed 400 round trip.
 */
function validate(): string | null {
  if (!form.templateId) return 'Choose a template.'
  const length = form.question.trim().length
  if (length < LIMITS.questionMin) return 'Enter a question for the council.'
  if (length > LIMITS.questionMax) {
    return `Question must be ${LIMITS.questionMax} characters or fewer.`
  }
  if (form.maxRounds < LIMITS.maxRoundsMin || form.maxRounds > LIMITS.maxRoundsMax) {
    return `Rounds must be between ${LIMITS.maxRoundsMin} and ${LIMITS.maxRoundsMax}.`
  }
  if (form.temperature < LIMITS.temperatureMin || form.temperature > LIMITS.temperatureMax) {
    return `Temperature must be between ${LIMITS.temperatureMin} and ${LIMITS.temperatureMax}.`
  }
  return null
}

async function submit(): Promise<void> {
  clientError.value = validate()
  serverErrors.value = {}
  apiError.value = null
  if (clientError.value) return

  const payload: CreateDebateRequest = {
    templateId: form.templateId,
    question: form.question.trim(),
    options: {
      maxRounds: form.maxRounds,
      temperature: form.temperature,
    },
  }

  submitting.value = true
  try {
    const debate = await create(payload)
    // The id goes in the URL so a reload recovers the view while the record still exists
    // (records are evicted after ~30 minutes server-side).
    await router.push(`/debates/${debate.debateId}`)
  } catch (error) {
    const parsed = error as ParsedApiError
    apiError.value = parsed
    serverErrors.value = parsed.fieldErrors ?? {}
  } finally {
    submitting.value = false
  }
}

onMounted(async () => {
  try {
    templates.value = await fetchTemplates()
  } catch {
    templates.value = []
  }
})
</script>

<style scoped>
.title {
  margin: 0 0 1.25rem;
  font-size: 1.35rem;
}

.form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 720px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.field-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

.field__label {
  font-size: 0.8rem;
  color: #9fb0d0;
  font-weight: 600;
}

.field__hint {
  font-size: 0.72rem;
  color: #7d879c;
}

.field__hint.is-over {
  color: #ff9a9a;
}

.input {
  background: #151922;
  color: #e6e6e6;
  border: 1px solid #2b3242;
  border-radius: 6px;
  padding: 0.5rem 0.6rem;
  font-size: 0.9rem;
  font-family: inherit;
}

.input--area {
  resize: vertical;
}

.actions {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}

.btn {
  background: #24314a;
  color: #dce6ff;
  border: 1px solid #33415c;
  border-radius: 6px;
  padding: 0.45rem 0.9rem;
  font-size: 0.85rem;
  cursor: pointer;
  text-decoration: none;
}

.btn--primary {
  background: #2f5fb3;
  border-color: #3d74cc;
  color: #fff;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.alert {
  padding: 0.8rem 1rem;
  border-radius: 8px;
  background: #151922;
  border: 1px solid #262b36;
  color: #9fb0d0;
  margin: 0;
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

code {
  background: #1c2230;
  padding: 0.1rem 0.3rem;
  border-radius: 4px;
}
</style>