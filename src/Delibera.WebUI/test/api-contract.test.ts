import { beforeEach, describe, expect, it } from 'vitest'
import { isStaticExport, resolveClientApiBase } from '../app/composables/useDebates'
import {
  API_VERSION_PREFIX,
  buildUpstreamHeaders,
  CORRELATION_HEADER,
  PROXY_PREFIX,
  TENANT_HEADER,
} from '../shared/deliberaApi'

/**
 * The BFF proxy and the app's SSR data path both call Delibera.Server, from two different
 * runtimes. These tests pin the contract they share — the header rules and the prefix — so the
 * two paths cannot drift apart unnoticed, and pin how the app decides which deployment mode it
 * is running in.
 */
describe('buildUpstreamHeaders', () => {
  it('always sends an explicit tenant', () => {
    // Delibera.Server falls back to the `default` tenant when the header is absent
    // (TenantResolutionMiddleware.cs:13-18), which would silently pool every user's debates.
    const headers = buildUpstreamHeaders({}, 'acme')
    expect(headers.get(TENANT_HEADER)).toBe('acme')
  })

  it('forwards an incoming tenant over the configured default', () => {
    const headers = buildUpstreamHeaders({ headers: { 'x-tenant-id': 'from-caller' } }, 'acme')
    expect(headers.get(TENANT_HEADER)).toBe('from-caller')
  })

  it('reads the tenant from a Headers instance too', () => {
    const incoming = new Headers({ 'X-Tenant-Id': 'from-headers' })
    expect(buildUpstreamHeaders({ headers: incoming }, 'acme').get(TENANT_HEADER)).toBe(
      'from-headers',
    )
  })

  it('lets the server generate a correlation id when the caller supplies none', () => {
    expect(buildUpstreamHeaders({}, 'acme').get(CORRELATION_HEADER)).toBeNull()
  })

  it('forwards a correlation id when the caller supplies one', () => {
    const headers = buildUpstreamHeaders({ headers: { 'X-Correlation-Id': 'abc123' } }, 'acme')
    expect(headers.get(CORRELATION_HEADER)).toBe('abc123')
  })

  it('negotiates SSE and only sets a content type when there is a body', () => {
    // Accept SSE so an intermediary does not buffer the stream the live view depends on.
    expect(buildUpstreamHeaders({}, 'acme').get('Accept')).toContain('text/event-stream')
    expect(buildUpstreamHeaders({}, 'acme').get('Content-Type')).toBeNull()
    expect(buildUpstreamHeaders({ body: { a: 1 } }, 'acme').get('Content-Type')).toBe(
      'application/json',
    )
  })
})

describe('prefixes', () => {
  it('keeps the proxy prefix distinct from the API version prefix', () => {
    // These are the two halves of `PROXY_PREFIX + '/debates'` -> `API_VERSION_PREFIX + '/debates'`.
    // Collapsing them into one constant is what would let a browser address the API directly.
    expect(PROXY_PREFIX).toBe('/api/delibera')
    expect(API_VERSION_PREFIX).toBe('/api/v1')
  })
})

describe('deployment mode', () => {
  beforeEach(() => {
    delete import.meta.env.NUXT_PUBLIC_DELIBERA_API_BASE
  })

  it('treats an unset public base as the container build, proxied through the BFF', () => {
    // The container build runs with no public API base: the browser must stay same-origin
    // because Delibera.Server registers no CORS policy.
    expect(isStaticExport()).toBe(false)
    expect(resolveClientApiBase()).toBe(PROXY_PREFIX)
  })

  it('treats a configured public base as the static export', () => {
    // The only build with no Nitro server to proxy through — which is exactly what pins
    // `useApiQuery` to client-side fetching and keeps baked data out of the prerendered HTML.
    import.meta.env.NUXT_PUBLIC_DELIBERA_API_BASE = 'https://api.example.com/'
    expect(isStaticExport()).toBe(true)
    expect(resolveClientApiBase()).toBe(`https://api.example.com${PROXY_PREFIX}`)
  })
})