/**
 * Data access for debates and templates.
 *
 * Every call targets the SAME-ORIGIN proxy route (`/api/delibera/...`), never the API
 * origin. `tenantId` is sent by the BFF from runtime config, not by the browser.
 */

import type {
  CreateDebateRequest,
  DebateResponse,
  DebateRoundDto,
  TemplateDto,
} from '#shared/types/delibera'
import { parseApiError } from './useApiError'

const BASE = '/api/delibera'

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${BASE}${path}`, {
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
  /** `GET /debates` — returns DebateResponse[]. */
  function list(filters: DebateListFilters = {}) {
    const query = new URLSearchParams()
    if (filters.templateId) query.set('templateId', filters.templateId)
    if (filters.status) query.set('status', filters.status)
    query.set('page', String(filters.page ?? 1))
    query.set('pageSize', String(filters.pageSize ?? 20))

    return request<DebateResponse[]>(`/debates?${query.toString()}`)
  }

  /** `GET /debates/{id}` — 404 once the ~30 min record eviction has run. */
  function get(id: string) {
    return request<DebateResponse>(`/debates/${encodeURIComponent(id)}`)
  }

  /**
   * `POST /debates/async` — returns immediately with Pending, then streams.
   *
   * The synchronous `POST /debates` exists too, but it holds the connection for the full
   * 150–200 s a debate takes; the async form is what makes the live view possible.
   */
  function create(payload: CreateDebateRequest) {
    return request<DebateResponse>('/debates/async', {
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
      await request<void>(`/debates/${encodeURIComponent(id)}`, { method: 'DELETE' })
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
      `/debates/${encodeURIComponent(id)}/rounds?page=${page}&pageSize=${pageSize}`,
    )
  }

  /** `GET /templates` — the source of truth for the template picker. Never hardcoded. */
  function templates() {
    return request<TemplateDto[]>('/templates')
  }

  /**
   * Markdown export URL. Built from the same-origin proxy rather than from
   * `DebateResponse.exportUrl`/`resultUrl`, which are absolute and computed from
   * Scheme://Host with no forwarded-header handling.
   */
  function exportUrl(id: string): string {
    return `${BASE}/debates/${encodeURIComponent(id)}/export/markdown`
  }

  /** SSE URL, same reasoning as exportUrl. */
  function streamUrl(id: string): string {
    return `${BASE}/debates/${encodeURIComponent(id)}/stream`
  }

  return { list, get, create, cancel, rounds, templates, exportUrl, streamUrl }
}