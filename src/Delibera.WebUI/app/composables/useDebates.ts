/**
 * Data access for debates and templates.
 *
 * Every call targets the SAME-ORIGIN proxy route (`/api/delibera/...`), never the API
 * origin. `tenantId` is sent by the BFF from runtime config, not by the browser.
 *
 * The one exception is a STATIC build (`nuxt generate`, i.e. GitHub Pages), where no Nitro
 * server exists and therefore no BFF route either — see `resolveApiBase()`.
 */

import type {
  CreateDebateRequest,
  DebateResponse,
  DebateRoundDto,
  TemplateDto,
} from '#shared/types/delibera'
import { parseApiError } from './useApiError'

/**
 * Resolves the API base, defaulting to the same-origin BFF.
 *
 * Read from `import.meta.env` rather than `useRuntimeConfig()` on purpose: `useDebates()` is
 * called from inside `useDebateStream`'s `start()` and poll timer, which run on mount and on
 * a timer — long after setup has unwound and Vue's current instance is gone, so a composable
 * context lookup there is unreliable. A build-time constant has no such dependency and is
 * exactly what a static export needs anyway.
 *
 * The constant only exists if `deliberaApiBase` is declared under `runtimeConfig.public` in
 * nuxt.config.ts. Nuxt substitutes `import.meta.env.NUXT_PUBLIC_*` for declared keys only, and
 * an undeclared name is not an error — the build stays green and every request keeps going to
 * the same origin. Do not rename one without the other.
 *
 * Empty (the default) → same-origin `/api/delibera`, which is the production container path:
 * `Delibera.Server` registers no CORS policy, so the browser must never call it directly.
 * Set `NUXT_PUBLIC_DELIBERA_API_BASE` to an absolute origin ONLY for a static build, and only
 * against a server that allows this origin via CORS.
 *
 * Note the tenant header in that case: EventSource cannot set headers, so the BFF no longer
 * supplies X-Tenant-Id and the API falls back to its `default` tenant.
 */
function resolveApiBase(): string {
  const configured = String(import.meta.env.NUXT_PUBLIC_DELIBERA_API_BASE ?? '').trim()
  return `${configured.replace(/\/+$/, '')}/api/delibera`
}

async function request<T>(base: string, path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${base}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...(init.body ? { 'Content-Type': 'application/json' } : {}),
      ...init.headers,
    },
  })

  if (!response.ok) throw await parseApiError(response)

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export interface DebateListFilters {
  templateId?: string
  status?: string
  page?: number
  pageSize?: number
}

export function useDebates() {
  const base = resolveApiBase()

  /** `GET /debates` — returns DebateResponse[]. */
  function list(filters: DebateListFilters = {}) {
    const query = new URLSearchParams()
    if (filters.templateId) query.set('templateId', filters.templateId)
    if (filters.status) query.set('status', filters.status)
    query.set('page', String(filters.page ?? 1))
    query.set('pageSize', String(filters.pageSize ?? 20))

    return request<DebateResponse[]>(base, `/debates?${query.toString()}`)
  }

  /** `GET /debates/{id}` — 404 once the ~30 min record eviction has run. */
  function get(id: string) {
    return request<DebateResponse>(base, `/debates/${encodeURIComponent(id)}`)
  }

  /**
   * `POST /debates/async` — returns immediately with Pending, then streams.
   *
   * The synchronous `POST /debates` exists too, but it holds the connection for the full
   * 150–200 s a debate takes; the async form is what makes the live view possible.
   */
  function create(payload: CreateDebateRequest) {
    return request<DebateResponse>(base, '/debates/async', {
      method: 'POST',
      body: JSON.stringify(payload),
    })
  }

  /**
   * `DELETE /debates/{id}` — 204 on success, but ALSO 404 when the debate had already
   * reached a terminal state (`DebateOrchestrationService.cs:145`). That second 404 is the
   * desired outcome from the user's point of view, so it is translated rather than raised.
   */
  async function cancel(id: string): Promise<'cancelled' | 'already-finished'> {
    try {
      await request<void>(base, `/debates/${encodeURIComponent(id)}`, { method: 'DELETE' })
      return 'cancelled'
    } catch (error) {
      if (typeof error === 'object' && error !== null && (error as { status?: number }).status === 404) {
        return 'already-finished'
      }
      throw error
    }
  }

  /** `GET /debates/{id}/rounds` — paginated snapshot, for a completed debate. */
  function rounds(id: string, page = 1, pageSize = 50) {
    return request<DebateRoundDto[]>(
      base,
      `/debates/${encodeURIComponent(id)}/rounds?page=${page}&pageSize=${pageSize}`,
    )
  }

  /** `GET /templates` — the source of truth for the template picker. Never hardcoded. */
  function templates() {
    return request<TemplateDto[]>(base, '/templates')
  }

  /**
   * Markdown export URL. Built from the resolved base rather than from
   * `DebateResponse.exportUrl`/`resultUrl`, which are absolute and computed from
   * Scheme://Host with no forwarded-header handling.
   */
  function exportUrl(id: string): string {
    return `${base}/debates/${encodeURIComponent(id)}/export/markdown`
  }

  /** SSE URL, same reasoning as exportUrl. */
  function streamUrl(id: string): string {
    return `${base}/debates/${encodeURIComponent(id)}/stream`
  }

  return { list, get, create, cancel, rounds, templates, exportUrl, streamUrl }
}