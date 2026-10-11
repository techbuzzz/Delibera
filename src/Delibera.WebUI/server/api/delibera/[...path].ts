/**
 * BFF catch-all: every browser call to the Delibera API goes through here.
 *
 * The browser NEVER talks to Delibera.Server directly — the server has no CORS policy, so a
 * cross-origin call would be blocked. Same-origin proxying is the fix, not a convenience.
 */

// Imported from `h3`, NOT from `nuxt/server` (Nuxt 4.6+).
//
// `nuxt/server` is a deliberate server-agnostic surface: it re-declares the request helpers
// against Nuxt's own `RequestEvent` and omits the response-writing helpers entirely, so that a
// non-Nitro server builder can satisfy it. That is the wrong trade for THIS route. A BFF whose
// job is forwarding an upstream body verbatim (`sendWebResponse`) or streaming SSE chunk by
// chunk (`sendStream`) needs exactly the Nitro-bound helpers `nuxt/server` abstracts away, and
// Nuxt's `RequestEvent` is not assignable to h3's `H3Event` — mixing the two surfaces fails
// typecheck with `TS2345`, not at runtime. The app is Nitro-pinned anyway
// (`nitro.preset: 'node-server'` in nuxt.config.ts).
//
// Revisit only if the proxy can stop writing responses directly.
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
// Explicit under `future.compatibilityVersion: 5`, which turns off the Nitro auto-import
// compatibility layer. `nitropack/runtime` rather than `#imports` so the dependency is stated
// instead of inferred — and it is the same path Nitro v3 keeps, so this survives Nuxt 5.
import { useRuntimeConfig } from 'nitropack/runtime'
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

  // Cancel the upstream call when the browser goes away.
  //
  // `timeout: 0` above means nothing else ever ends this request, and a debate runs for
  // 150–200 s with an SSE stream open for longer still. Navigating off the page, closing the
  // tab, or simply having EventSource reconnect all end the client side while the upstream
  // connection stays open and burns a server-side stream — one leaked connection per round of
  // user navigation, for the whole life of the debate.
  //
  // `res.close` is the correct hook rather than `req.close`: the request stream ends as soon
  // as the body is consumed, which for a body-less GET is immediately, so listening there
  // would abort every call in this route. The guard on `writableEnded` keeps the normal
  // completion path — where close fires *after* the response was fully written — from
  // cancelling a request that already succeeded.
  const abort = new AbortController()
  event.node.res.on('close', () => {
    if (!event.node.res.writableEnded) abort.abort()
  })

  // A debate legitimately runs 150–200 s and an SSE stream stays open far longer, so
  // Nitro's default fetch timeout would cut both off mid-flight. `timeout` is a Nitro
  // extension to RequestInit, hence the widened type rather than a bare `as RequestInit`
  // (which would still be checked against the DOM type before the assertion applied).
  const upstreamInit = {
    method,
    body: upstreamBody,
    signal: abort.signal,
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
    // The client hanging up is not an upstream failure. Reporting 502 here would describe a
    // problem the user cannot act on — and the response they would receive is one nobody is
    // left to read, because the socket they asked to close is the one this would be written
    // to.
    if (abort.signal.aborted) return

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
    try {
      await sendStream(event, upstream.body)
    } catch (error) {
      // Same reasoning as the fetch guard: an aborted stream means the browser navigated
      // away or reconnected, and the resulting read error on `upstream.body` is the abort
      // working as intended rather than a proxy fault worth surfacing.
      if (!abort.signal.aborted) throw error
    }
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