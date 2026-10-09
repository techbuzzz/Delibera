/**
 * Health passthrough for the UI's own status badge.
 *
 * Delibera.Server's /api/v1/health uses the default health-check writer, which emits PLAIN
 * TEXT ("Healthy", or "Degraded: ..."), not JSON. Proxying it verbatim keeps the badge
 * honest instead of guessing at a shape the server never produces.
 */

import { defineEventHandler } from 'h3'
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