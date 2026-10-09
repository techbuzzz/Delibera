/**
 * Upstream fetch helper — the single choke point for every call to Delibera.Server.
 *
 * WHY A PROXY EXISTS AT ALL
 * Delibera.Server registers no CORS policy (no AddCors/UseCors anywhere in the repo), so it
 * emits no `Access-Control-Allow-Origin`. A browser talking to the API origin directly is
 * blocked on every request, GETs included. Routing everything through a Nitro route keeps
 * the browser on one origin and sidesteps the problem entirely.
 *
 * It also fixes absolute-URL drift: `DebateMapper` builds `streamUrl`/`resultUrl` from
 * `Scheme://Host` with no forwarded-header handling, so they point at the wrong host behind
 * a proxy or ingress. The UI composes its own URLs from same-origin paths instead.
 */

import type { H3Event } from 'h3'

/** Client-facing prefix that maps onto the server's `/api/v1` group. */
export const PROXY_PREFIX = '/api/delibera'
export const API_VERSION_PREFIX = '/api/v1'

export class UpstreamError extends Error {
  constructor(
    message: string,
    readonly detail: string,
  ) {
    super(message)
    this.name = 'UpstreamError'
  }
}

/** Strips a leading slash so paths can be joined without producing a double separator. */
function trimLeadingSlash(path: string): string {
  return path.startsWith('/') ? path.slice(1) : path
}

/**
 * Maps a client-facing proxy path onto the upstream API path.
 *
 * `/api/delibera/debates/abc` -> `http://host:8080/api/v1/debates/abc`
 *
 * The path is attacker-controlled, so it is validated rather than trusted. Two things are
 * rejected outright:
 *   - `..` segments, which would let a caller walk out of `/api/v1` and reach any other
 *     route on the host;
 *   - an encoded separator (`%2f`, `%5c`) or a backslash, which survive naive checks and
 *     then decode into `../` after the upstream has already accepted the path.
 *
 * Rejecting rather than sanitising is deliberate: silently rewriting a traversal into a
 * harmless path would let a broken client look like it worked.
 */
export function resolveUpstreamPath(
  clientPath: string | undefined,
  baseUrl: string,
): { url: string; relative: string } {
  let path = trimLeadingSlash(clientPath ?? '')

  // A caller may send the version prefix explicitly; the proxy owns it, so it is not
  // duplicated upstream. The slash is consumed with the prefix — slicing only the prefix
  // would leave a leading separator and produce ".../api/v1//debates".
  const versionPrefix = trimLeadingSlash(API_VERSION_PREFIX)
  if (path === versionPrefix) {
    path = ''
  } else if (path.startsWith(`${versionPrefix}/`)) {
    // +1 consumes the separating slash as well as the prefix; slicing only the prefix
    // length leaves "/debates", which becomes ".../api/v1//debates".
    path = path.slice(versionPrefix.length + 1)
  }

  const hasDotDot = path.split('/').some((segment) => segment === '..')
  const hasEncodedSeparator = /%(?:2f|5c)/i.test(path)
  const hasBackslash = path.includes('\\')

  if (hasDotDot || hasEncodedSeparator || hasBackslash) {
    throw new UpstreamError(
      'Rejected proxy path: traversal or encoded separators are not allowed',
      `The path '${clientPath}' contains traversal or encoded separators.`,
    )
  }

  const relative = `${API_VERSION_PREFIX}/${path}`
  const url = `${baseUrl.replace(/\/+$/, '')}${relative}`
  return { url, relative }
}

/** Resolves the upstream base, failing loudly rather than silently hitting localhost. */
export function resolveApiBase(configured: string | undefined): string {
  const base = (configured ?? '').trim()
  if (!base) {
    throw new UpstreamError(
      'Delibera API base is not configured',
      'Set NUXT_DELIBERA_API_BASE (for example http://delibera-server:8080).',
    )
  }
  return base.replace(/\/+$/, '')
}

export interface UpstreamRequestOptions {
  method?: string
  query?: Record<string, string | undefined>
  body?: unknown
  /**
   * Incoming request headers. Typed loosely because h3's `getRequestHeaders` returns a
   * partial `Record<HTTPHeaderName, string | undefined>`, not a `Headers` instance; a
   * `Headers` is also accepted so callers can pass one directly.
   */
  headers?: Headers | Record<string, string | string[] | undefined>
}

function readIncomingHeader(
  headers: UpstreamRequestOptions['headers'],
  name: string,
): string | undefined {
  if (!headers) return undefined
  if (headers instanceof Headers) return headers.get(name) ?? undefined

  const entry = Object.entries(headers).find(
    ([key]) => key.toLowerCase() === name.toLowerCase(),
  )?.[1]

  if (Array.isArray(entry)) return entry[0]
  return entry
}

/**
 * Builds the outgoing headers.
 *
 * `X-Tenant-Id` is always sent explicitly. The server falls back to the `default` tenant
 * when the header is absent (`TenantResolutionMiddleware.cs:13-18`), and a multi-tenant
 * deployment would otherwise silently pool every user's debates into one bucket.
 *
 * `X-Correlation-Id` is forwarded when the caller supplies one and left to the server to
 * generate otherwise (`CorrelationIdMiddleware.cs:10`); the server always echoes it on the
 * response, which the UI surfaces on errors.
 */
export function buildUpstreamHeaders(
  options: UpstreamRequestOptions,
  defaultTenantId: string,
): Headers {
  const headers = new Headers()

  headers.set('X-Tenant-Id', readIncomingHeader(options.headers, 'X-Tenant-Id') ?? defaultTenantId)

  const correlationId = readIncomingHeader(options.headers, 'X-Correlation-Id')
  if (correlationId) headers.set('X-Correlation-Id', correlationId)

  if (options.body !== undefined) headers.set('Content-Type', 'application/json')

  // Accept SSE so the event stream is not buffered by an intermediary that negotiates it.
  headers.set('Accept', 'text/event-stream, application/json')

  return headers
}

/**
 * True when the upstream response is a Server-Sent Events stream.
 *
 * Named `isEventStreamResponse` rather than `isEventStream` on purpose: server/utils is
 * auto-imported by Nitro, and h3 exports its own `isEventStream` (which tests a *request*
 * body for an event-stream content type). Two different meanings under one name is a
 * silent-wrong-behaviour waiting to happen.
 */
export function isEventStreamResponse(upstream: Response): boolean {
  return (upstream.headers.get('content-type') ?? '').includes('text/event-stream')
}

/** Serialises a query object, dropping undefined values. */
export function toSearchParams(
  query: Record<string, string | undefined> | undefined,
): URLSearchParams {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined && value !== null && value !== '') {
      params.set(key, value)
    }
  }
  return params
}