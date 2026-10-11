/**
 * Path and header contracts shared by the BFF proxy and the app's own data layer.
 *
 * These live in `shared/` rather than `server/utils/` because two different runtimes now need
 * them: the Nitro proxy route (`server/api/delibera/[...path].ts`) and the SSR branch of
 * `useApiQuery`, which fetches the API directly from the server during prerender/SSR. Keeping
 * the strings in one place is the whole point — a tenant header that exists in only one of the
 * two paths is exactly the bug the proxy's own comment warns about.
 */

/** Client-facing prefix that maps onto the server's `/api/v1` group. */
export const PROXY_PREFIX = '/api/delibera'
export const API_VERSION_PREFIX = '/api/v1'

export const TENANT_HEADER = 'X-Tenant-Id'
export const CORRELATION_HEADER = 'X-Correlation-Id'

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
 * Builds the outgoing headers for a call to Delibera.Server.
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

  headers.set(TENANT_HEADER, readIncomingHeader(options.headers, TENANT_HEADER) ?? defaultTenantId)

  const correlationId = readIncomingHeader(options.headers, CORRELATION_HEADER)
  if (correlationId) headers.set(CORRELATION_HEADER, correlationId)

  if (options.body !== undefined) headers.set('Content-Type', 'application/json')

  // Accept SSE so the event stream is not buffered by an intermediary that negotiates it.
  headers.set('Accept', 'text/event-stream, application/json')

  return headers
}