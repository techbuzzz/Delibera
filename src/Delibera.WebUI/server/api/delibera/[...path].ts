/**
 * BFF catch-all: every browser call to the Delibera API goes through here.
 *
 * The browser NEVER talks to Delibera.Server directly — the server has no CORS policy, so a
 * cross-origin call would be blocked. Same-origin proxying is the fix, not a convenience.
 */

import {
  defineEventHandler,
  getQuery,
  getRequestHeaders,
  readBody,
  sendStream,
  sendWebResponse,
  setResponseHeader,
  setResponseStatus,
} from 'h3'
import {
  buildUpstreamHeaders,
  isEventStreamResponse,
  resolveApiBase,
  resolveUpstreamPath,
  toSearchParams,
  UpstreamError,
} from '../../utils/delibera'

/**
 * Re-encodes the incoming request body for the upstream fetch.
 *
 * `readBody` parses the payload (JSON in, plain object out), and `fetch` stringifies a
 * non-string body with `String(value)` — which yields the literal `"[object Object]"`. The
 * upstream then received invalid JSON and answered 400 "Bad Request" with no field detail,
 * for every POST through the proxy.
 *
 * A string body is passed through untouched: `readBody` returns the raw text for
 * non-JSON content types, and re-encoding it would corrupt it.
 */
function encodeUpstreamBody(parsed: unknown): string | undefined {
  if (parsed === undefined || parsed === null) return undefined
  if (typeof parsed === 'string') return parsed
  if (typeof parsed === 'object' && Object.keys(parsed as object).length === 0) {
    // An empty JSON object carries no information the server needs, and sending "{}" for
    // a body-less request is how a DELETE ends up asking the API to validate one.
    return undefined
  }
  return JSON.stringify(parsed)
}

export default defineEventHandler(async (event) => {
  const config = useRuntimeConfig(event)

  let apiBase: string
  let url: string
  try {
    apiBase = resolveApiBase(config.deliberaApiBase)
    const clientPath = event.context.params?.path
    url = resolveUpstreamPath(clientPath, apiBase).url
  } catch (error) {
    // A misconfigured proxy is a 500 with a precise reason, not a confusing upstream failure.
    if (error instanceof UpstreamError) {
      setResponseStatus(event, 500)
      return {
        title: error.message,
        detail: error.detail,
        status: 500,
      }
    }
    throw error
  }

  const query = toSearchParams(
    Object.fromEntries(
      Object.entries(getQuery(event)).map(([k, v]) => [k, Array.isArray(v) ? v[0] : (v as string)]),
    ),
  )
  const search = query.toString()
  const target = search ? `${url}?${search}` : url

  const method = event.method ?? 'GET'
  const hasBody = method !== 'GET' && method !== 'HEAD'
  const upstreamBody = hasBody ? encodeUpstreamBody(await readBody(event)) : undefined

  // A debate legitimately runs 150–200 s and an SSE stream stays open far longer, so
  // Nitro's default fetch timeout would cut both off mid-flight. `timeout` is a Nitro
  // extension to RequestInit, hence the widened type rather than a bare `as RequestInit`
  // (which would still be checked against the DOM type before the assertion applied).
  const upstreamInit = {
    method,
    body: upstreamBody,
    headers: buildUpstreamHeaders(
      {
        method,
        body: upstreamBody,
        headers: getRequestHeaders(event),
      },
      config.public.tenantId,
    ),
    timeout: 0,
  } as RequestInit & { timeout?: number }

  let upstream: Response
  try {
    upstream = await fetch(target, upstreamInit)
  } catch (error) {
    setResponseStatus(event, 502)
    return {
      title: 'Delibera API is unreachable',
      detail:
        `Could not reach ${apiBase}. Is delibera-server running? ` +
        `(${error instanceof Error ? error.message : String(error)})`,
      status: 502,
    }
  }

  // The correlation id is echoed for every non-stream response below, and the status is
  // carried by sendWebResponse itself — setting it here as well would be redundant.

  // ── SSE passthrough ────────────────────────────────────────────────────────
  // Forwarding the upstream body as it arrives is the whole point: buffering would delay
  // every round until the debate finished — 150–200 s — and defeat the live view entirely.
  // `sendStream` (not `proxyStream`, which does not exist in this Nitro version) writes each
  // chunk through and ends the response when the upstream does.
  if (isEventStreamResponse(upstream) && upstream.body) {
    setResponseHeader(event, 'Content-Type', 'text/event-stream')
    setResponseHeader(event, 'Cache-Control', 'no-cache')
    setResponseHeader(event, 'X-Accel-Buffering', 'no')
    await sendStream(event, upstream.body)
    return
  }

  // Bodies are forwarded verbatim, and this MUST go through sendWebResponse.
  //
  // Returning the ArrayBuffer directly does not work: h3 treats an unrecognised return value
  // as data to JSON-serialise, and JSON.stringify(new ArrayBuffer()) is "{}" — so the client
  // received an empty object under a 200 status instead of the payload. That failure is
  // silent, because status and Content-Type both looked correct.
  //
  // Passing the response through also preserves Delibera.Server's deliberately mixed error
  // shapes — JSON ProblemDetails for validation, text/plain for Conflict<string> and for the
  // bare 404s from UseStatusCodePages (registered with no formatter). The client parses by
  // content type; rewriting the body here would destroy that signal.
  const headers = new Headers()
  const contentType = upstream.headers.get('Content-Type')
  if (contentType) headers.set('Content-Type', contentType)

  const correlationId = upstream.headers.get('X-Correlation-Id')
  if (correlationId) headers.set('X-Correlation-Id', correlationId)

  return sendWebResponse(
    event,
    new Response(upstream.body, { status: upstream.status, headers }),
  )
})