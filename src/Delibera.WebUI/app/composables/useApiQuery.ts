/**
 * `useAsyncData` wrapper for API reads, with the two deployment modes kept explicitly apart.
 *
 * WHY THIS EXISTS
 * Every page used to load its data in `onMounted` with a bare `fetch`. With `ssr: true` that
 * means the server returns a shell — heading, no rows — and the reader waits for the bundle
 * to arrive before the first request even starts. Moving to `useAsyncData` puts the same data
 * in the first HTML response and lets Nuxt carry it across the hydration boundary, which is
 * the entire reason to run a server in the first place.
 *
 * TWO MODES, ONE API
 * The app ships two builds from one source, and they must not share a data path:
 *
 *   Container (SSR) — the Nitro server can reach Delibera.Server directly and there is no CORS
 *   constraint at all, so the SSR pass calls the upstream itself. Going through the BFF would
 *   mean the server making a loopback request to its own origin, which breaks wherever the
 *   public host is not resolvable from inside the container — TLS terminated upstream, an
 *   ingress-only hostname — and buys nothing, since there is no browser involved to protect.
 *
 *   Static export (GitHub Pages) — there is no server to fetch anything, and prerendering runs
 *   the same SSR pass at BUILD time. Fetching here would bake the debates that existed when
 *   the site was built into `index.html`, ship them as the payload, and then reuse that
 *   payload on the client instead of re-fetching: a deployed site that looks populated and is
 *   frozen at its deploy date. So this mode is pinned to `server: false` and keeps the
 *   current client-only behaviour.
 *
 * THE PRERENDER CAVEAT, WHICH IS NOT PAGES-SPECIFIC
 * `isStaticExport()` is necessary but not sufficient. `nitro.prerender.routes` is honoured by
 * every `nuxt build`, container included: Nitro renders those routes during the build and then
 * serves the static file in preference to rendering them again. A page listed there behaves
 * exactly like the static export above even though `isStaticExport()` is false — its server
 * pass ran on the build machine. So `server` is passed explicitly per call site rather than
 * derived, and the two prerendered pages ('/' and '/debates/new') opt out. `/debates/[id]` is
 * not prerendered — its ids only exist at runtime — so it takes the full SSR path.
 *
 * ERRORS ARE DATA, NOT EXCEPTIONS
 * `useAsyncData` treats a thrown handler as a request failure: Nuxt renders `error.vue` and the
 * page-level alert the UI already has never appears. Every call therefore resolves to a
 * `{ value, error }` pair, and the page decides what a failure looks like — an inline alert for
 * a 502, the same alert plus an expiry note for a 404. This keeps the existing, deliberate
 * error UI intact instead of replacing it with a framework-level error page.
 */

import type { ParsedApiError } from './useApiError'
import { API_VERSION_PREFIX, buildUpstreamHeaders } from '#shared/deliberaApi'
import { apiRequest, isStaticExport, resolveClientApiBase } from './useDebates'

export interface ApiQueryResult<T> {
  value: T | null
  error: ParsedApiError | null
}

export interface ApiQueryOptions {
  /**
   * Allow the server to run this handler. Defaults to `true` — except for a static export,
   * where it is always off (see the module comment).
   */
  server?: boolean
}

/** Normalises anything thrown into the `ParsedApiError` shape the pages already render. */
function toParsedError(error: unknown): ParsedApiError {
  if (
    typeof error === 'object' &&
    error !== null &&
    typeof (error as ParsedApiError).status === 'number'
  ) {
    return error as ParsedApiError
  }

  // A transport failure (API down, DNS, refused connection) never reached `parseApiError`,
  // so there is no status and no server wording to show. 502 is what this page would have
  // received through the BFF for exactly the same failure.
  return {
    status: 502,
    message: 'The Delibera API is unreachable.',
    detail: error instanceof Error ? error.message : String(error),
    fieldErrors: {},
  }
}

/**
 * Runs one API read and returns the outcome as a value.
 *
 * On the server this targets the upstream origin directly, with the tenant header the BFF
 * would otherwise have added — `buildUpstreamHeaders` is shared with the proxy precisely so
 * the two paths cannot disagree about it.
 */
async function fetchQueryValue<T>(path: string): Promise<T> {
  if (import.meta.server) {
    const config = useRuntimeConfig()
    const base = String(config.deliberaApiBase ?? '').trim().replace(/\/+$/, '')
    const headers = buildUpstreamHeaders({}, String(config.public.tenantId))

    return apiRequest<T>(`${base}${API_VERSION_PREFIX}`, path, { headers })
  }

  return apiRequest<T>(resolveClientApiBase(), path)
}

/**
 * Reactive-friendly wrapper around `useAsyncData`.
 *
 * `key` may be a getter, which is what makes filtering and pagination work: changing the key
 * re-runs the handler automatically, so a page can express "these inputs produce this list"
 * instead of hand-wiring a watcher to a manual reload.
 *
 * Awaited rather than fire-and-forget, so a caller can read the first result straight after
 * the call — `onMounted` deciding whether to open a stream must not have to guess whether the
 * record has landed yet. This is the documented Nuxt shape for a page-level data read.
 */
export async function useApiQuery<T>(
  key: string | (() => string),
  path: string | (() => string),
  options: ApiQueryOptions = {},
) {
  const server = options.server ?? !isStaticExport()

  const { data, pending, refresh } = await useAsyncData<ApiQueryResult<T>>(
    key,
    async () => {
      try {
        const value = await fetchQueryValue<T>(typeof path === 'function' ? path() : path)
        return { value, error: null }
      } catch (error) {
        return { value: null, error: toParsedError(error) }
      }
    },
    {
      server,
      // Without this the first render sees `undefined` and every `data.value.x` below it
      // needs a guard it should not have needed.
      default: () => ({ value: null, error: null }),

      /**
       * Reuse the server payload only when it holds data.
       *
       * This handler resolves on failure instead of throwing, so an SSR render that hit a
       * dead API writes `error: {...}` into `__NUXT_DATA__` — and Nuxt would then hydrate from
       * that cached result and never refetch. The reader would load the page at the exact
       * moment the API was down, see "unreachable", and keep seeing it after the API came
       * back, until they thought to press Refresh. Discarding a cached failure costs one extra
       * request on that rare path and makes the page self-healing; a cached success is still
       * reused, which is the whole point of the payload.
       */
      getCachedData: (key, nuxtApp) => {
        const cached = nuxtApp.payload.data[key] ?? nuxtApp.static.data[key]
        if (cached && typeof cached === 'object' && 'error' in cached && cached.error) {
          return undefined
        }
        return cached
      },
    },
  )

  return {
    /** The payload, or null before the first successful load and after a failed one. */
    data: computed(() => data.value?.value ?? null),
    /** The failure, or null. Kept separate from `data` so a retry clears it on its own. */
    error: computed(() => data.value?.error ?? null),
    pending,
    refresh,
  }
}