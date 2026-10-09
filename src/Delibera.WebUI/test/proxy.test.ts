import { describe, expect, it } from 'vitest'
import { resolveUpstreamPath, UpstreamError, toSearchParams } from '../server/utils/delibera'

describe('resolveUpstreamPath', () => {
  const base = 'http://delibera-server:8080'

  it('maps the proxy prefix onto the server version prefix', () => {
    const { url, relative } = resolveUpstreamPath('debates/abc', base)
    expect(url).toBe('http://delibera-server:8080/api/v1/debates/abc')
    expect(relative).toBe('/api/v1/debates/abc')
  })

  it('tolerates a leading slash', () => {
    expect(resolveUpstreamPath('/debates', base).url).toBe(
      'http://delibera-server:8080/api/v1/debates',
    )
  })

  it('does not double the prefix when the caller sends it explicitly', () => {
    expect(resolveUpstreamPath('/api/v1/debates', base).url).toBe(
      'http://delibera-server:8080/api/v1/debates',
    )
  })

  it('strips a trailing slash from the configured base', () => {
    expect(resolveUpstreamPath('debates', 'http://delibera-server:8080/').url).toBe(
      'http://delibera-server:8080/api/v1/debates',
    )
  })

  it('handles an empty path', () => {
    expect(resolveUpstreamPath('', base).url).toBe('http://delibera-server:8080/api/v1/')
  })

  it('handles an undefined path', () => {
    expect(resolveUpstreamPath(undefined, base).relative).toBe('/api/v1/')
  })

  // ── Open-proxy guard ───────────────────────────────────────────────────────
  // The path is attacker-controlled. Without these checks a caller could walk out of
  // /api/v1 and reach any other route on the host.
  it('rejects traversal segments', () => {
    expect(() => resolveUpstreamPath('../../admin', base)).toThrow(UpstreamError)
    expect(() => resolveUpstreamPath('debates/../../secret', base)).toThrow(UpstreamError)
  })

  it('rejects percent-encoded separators, which decode after the upstream accepts them', () => {
    expect(() => resolveUpstreamPath('debates%2f..%2fadmin', base)).toThrow(UpstreamError)
    expect(() => resolveUpstreamPath('debates%5c..%5cadmin', base)).toThrow(UpstreamError)
    // Uppercase hex escapes the same way.
    expect(() => resolveUpstreamPath('a%2F..%2Fb', base)).toThrow(UpstreamError)
  })

  it('rejects backslashes', () => {
    expect(() => resolveUpstreamPath('debates\\..\\admin', base)).toThrow(UpstreamError)
  })

  it('allows a literal segment that merely contains dots', () => {
    // ".." is traversal; "..." or "a..b" are not. A blanket substring check would reject
    // legitimate ids for no reason.
    expect(resolveUpstreamPath('debates/abc..def', base).url).toBe(
      'http://delibera-server:8080/api/v1/debates/abc..def',
    )
  })

  it('names the rejected path in the error detail', () => {
    expect(() => resolveUpstreamPath('../x', base)).toThrow(/traversal/i)
  })
})

describe('toSearchParams', () => {
  it('drops undefined and empty values', () => {
    const params = toSearchParams({ page: '1', templateId: undefined, status: '' })
    expect(params.toString()).toBe('page=1')
  })

  it('returns empty params for undefined input', () => {
    expect(toSearchParams(undefined).toString()).toBe('')
  })
})