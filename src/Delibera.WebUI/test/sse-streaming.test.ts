/**
 * @vitest-environment node
 *
 * Proves the BFF forwards Server-Sent Events incrementally instead of buffering.
 *
 * This is the single behaviour the whole live view depends on. A proxy that buffered would
 * look correct in every functional test and still deliver every round 150-200 s late, after
 * the debate had already finished — so it is asserted against a real socket, with the
 * upstream deliberately withholding its final event.
 *
 * Runs the BUILT Nitro server (.output/server/index.mjs), because the streaming path only
 * exists in the production bundle.
 *
 * Node environment (not the project default happy-dom) because this test makes real network
 * calls against localhost; happy-dom substitutes its own fetch, which does not behave like
 * the streaming one.
 */

import { spawn } from 'node:child_process'
import { createServer } from 'node:http'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const here = dirname(fileURLToPath(import.meta.url))
const entry = resolve(here, '..', '.output', 'server', 'index.mjs')

const UPSTREAM_PORT = 5199
const BFF_PORT = 3199

/** A round is released ~1.5 s before the terminal event, so an early read proves streaming. */
const ROUND_AT_MS = 300
const COMPLETED_AT_MS = 3000

let upstream: ReturnType<typeof createServer>
let bff: ReturnType<typeof spawn> | undefined

/** Bodies the upstream actually received, so POST forwarding can be asserted. */
const receivedBodies: string[] = []

/**
 * Readiness probe.
 *
 * Deliberately accepts any HTTP response, including 503: `/api/health` reports 503 while
 * the upstream is unreachable, and the upstream is not listening yet at this point. Treating
 * a non-2xx as "not ready" makes the probe wait for a condition it can never satisfy.
 */
async function waitForHttp(url: string, timeoutMs = 30_000): Promise<void> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url)
      await response.arrayBuffer()
      return
    } catch {
      // connection refused — not up yet
    }
    await new Promise((r) => setTimeout(r, 200))
  }
  throw new Error(`timed out waiting for ${url}`)
}

describe('SSE proxy streaming', () => {
  beforeAll(async () => {
    if (!existsSync(entry)) {
      throw new Error(`built server not found at ${entry} — run "npm run build" first`)
    }

    // Upstream that echoes what it received, serves a JSON list, and emits SSE rounds
    // early while holding the connection open.
    upstream = createServer((req, res) => {
      if (req.method === 'POST') {
        const chunks: Buffer[] = []
        req.on('data', (c: Buffer) => chunks.push(c))
        req.on('end', () => {
          const raw = Buffer.concat(chunks).toString('utf8')
          receivedBodies.push(raw)
          // Mirrors the real API's 400 on an unparseable body, so a broken proxy produces a
          // genuine failure here rather than a silent success.
          try {
            JSON.parse(raw)
          } catch {
            res.writeHead(400, { 'Content-Type': 'application/problem+json' })
            res.end(JSON.stringify({ title: 'Bad Request', status: 400 }))
            return
          }
          res.writeHead(202, { 'Content-Type': 'application/json' })
          res.end(JSON.stringify({ debateId: 'test-debate', status: 'Pending' }))
        })
        return
      }

      if (req.url?.startsWith('/api/v1/templates')) {
        res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' })
        res.end(
          JSON.stringify([
            {
              templateId: 'risk-committee',
              displayName: 'Risk Committee Council',
              strategy: 'ConsensusDebate',
              defaultMaxRounds: 5,
              memberRoles: ['RiskManager'],
              votingStrategy: 'Majority',
              ragEnabled: false,
              operatorEnabled: false,
            },
          ]),
        )
        return
      }

      res.writeHead(200, {
        'Content-Type': 'text/event-stream',
        'Cache-Control': 'no-cache',
        'X-Accel-Buffering': 'no',
      })

      setTimeout(() => {
        res.write(
          `event: debate-round\ndata: ${JSON.stringify({
            roundNumber: 1,
            isFinal: false,
            strategy: 'StandardDebateStrategy',
            messages: [{ role: 'Analyst', content: 'hello', modelName: '' }],
          })}\n\n`,
        )
      }, ROUND_AT_MS)

      setTimeout(() => {
        res.write(
          `event: debate-completed\ndata: ${JSON.stringify({
            debateId: 'test-debate',
            status: 'Completed',
            verdict: 'Ship it',
          })}\n\n`,
        )
        res.end()
      }, COMPLETED_AT_MS)
    })

    await new Promise<void>((r) => upstream.listen(UPSTREAM_PORT, '127.0.0.1', r))

    bff = spawn(process.execPath, [entry], {
      env: {
        ...process.env,
        NODE_ENV: 'production',
        NITRO_PORT: String(BFF_PORT),
        NITRO_HOST: '127.0.0.1',
        NUXT_DELIBERA_API_BASE: `http://127.0.0.1:${UPSTREAM_PORT}`,
      },
      stdio: 'ignore',
    })

    await waitForHttp(`http://127.0.0.1:${BFF_PORT}/api/health`)
  }, 60_000)

  afterAll(async () => {
    bff?.kill()
    if (upstream) await new Promise<void>((r) => upstream.close(() => r()))
  })

  it('delivers a round before the upstream stream ends', async () => {
    const response = await fetch(
      `http://127.0.0.1:${BFF_PORT}/api/delibera/debates/test-debate/stream`,
    )

    expect(response.status).toBe(200)
    // Without this header the browser EventSource would not connect at all.
    expect(response.headers.get('content-type')).toContain('text/event-stream')

    const reader = response.body!.getReader()
    const decoder = new TextDecoder()

    let received = ''
    const startedAt = Date.now()

    // Read the FIRST chunk only. A buffering proxy would not emit anything until the
    // upstream finished at COMPLETED_AT_MS.
    const first = await reader.read()
    received += decoder.decode(first.value, { stream: true })
    const elapsed = Date.now() - startedAt

    expect(received).toContain('debate-round')
    expect(elapsed).toBeLessThan(
      COMPLETED_AT_MS - 500,
      `first chunk arrived after ${elapsed}ms — the proxy buffered the stream`,
    )

    // Drain the rest so the socket closes cleanly.
    while (true) {
      const chunk = await reader.read()
      if (chunk.done) break
      received += decoder.decode(chunk.value, { stream: true })
    }

    expect(received).toContain('debate-completed')
  }, 20_000)

  // Regression guard for a failure that looked like success: the route originally returned
  // the upstream ArrayBuffer directly, which h3 JSON-serialises — and
  // JSON.stringify(new ArrayBuffer()) is "{}". The client got a 200 with an empty object,
  // and only a real request against a real API revealed it.
  it('forwards a JSON body intact rather than serialising it', async () => {
    const response = await fetch(`http://127.0.0.1:${BFF_PORT}/api/delibera/templates`)

    expect(response.status).toBe(200)
    expect(response.headers.get('content-type')).toContain('json')

    const body = await response.json()
    expect(Array.isArray(body)).toBe(true)
    expect(body.length).toBeGreaterThan(0)
    expect(body[0]).toHaveProperty('templateId')
  }, 20_000)

  // Regression guard for the highest-impact bug found during UI testing: readBody returns
  // a PARSED object, and fetch stringifies a non-string body with String(value) — producing
  // the literal "[object Object]". Every POST through the proxy answered 400 "Bad Request"
  // with no field detail, while the same POST sent directly to the API returned 202.
  it('forwards a POST body as valid JSON', async () => {
    const payload = {
      templateId: 'risk-committee',
      question: 'proxy body probe',
      options: { maxRounds: 2, temperature: 0.7 },
    }

    const response = await fetch(`http://127.0.0.1:${BFF_PORT}/api/delibera/debates/async`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })

    // The upstream returns 400 for unparseable JSON, so a non-202 here means the body
    // arrived mangled.
    expect(response.status).toBe(202)

    const received = receivedBodies.at(-1)
    expect(received).toBeTruthy()
    expect(received).not.toContain('[object Object]')
    expect(JSON.parse(received!)).toEqual(payload)
  }, 20_000)
})