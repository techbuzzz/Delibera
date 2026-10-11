/**
 * Health passthrough for the UI's own status badge.
 *
 * Delibera.Server's /api/v1/health uses the default health-check writer, which emits PLAIN
 * TEXT ("Healthy", or "Degraded: ..."), not JSON. Proxying it verbatim keeps the badge
 * honest instead of guessing at a shape the server never produces.
 */

// From `h3`, not `nuxt/server` — see the note in server/api/delibera/[...path].ts. This route
// only reads a body-less GET and returns a plain object, but the `setResponseStatus` call below
// resolves to the Nitro auto-import, which is h3's, so the handler event must stay h3-typed.
import { defineEventHandler } from 'h3'
// Explicit under `future.compatibilityVersion: 5` — see server/api/delibera/[...path].ts.
import { useRuntimeConfig } from 'nitropack/runtime'
import { resolveApiBase, resolveUpstreamPath } from '../utils/delibera'

export default defineEventHandler(async (event) => {
  const config = useRuntimeConfig(event)

  try {
    const base = resolveApiBase(config.deliberaApiBase)
    const { url } = resolveUpstreamPath('health', base)

    // No `timeout` here: this probe is deliberately short-lived and must fail fast so a dead
// API shows as "Offline" rather than leaving the badge spinning.
    const upstream = await fetch(url, {
      signal: AbortSignal.timeout(5000),
    })
    const body = (await upstream.text()).trim()

    setResponseStatus(event, upstream.ok ? 200 : 503)
    return {
      ok: upstream.ok,
      status: body,
      // Lets the UI distinguish "server down" from "server up but reporting Degraded".
      degraded: !upstream.ok || body.toLowerCase().startsWith('degraded'),
    }
  } catch {
    setResponseStatus(event, 503)
    return { ok: false, status: 'unreachable', degraded: true }
  }
})