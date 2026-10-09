/**
 * Live debate stream over Server-Sent Events, with a polling fallback.
 *
 * Native EventSource is used rather than a fetch-based reader because it reconnects on its
 * own and, critically, cannot set request headers — which is fine here, since the BFF
 * resolves X-Tenant-Id and X-Correlation-Id server-side.
 */

import type {
  DebateCancelledEvent,
  DebateCompletedEvent,
  DebateErrorEvent,
  DebateResponse,
  DebateRoundDto,
} from '#shared/types/delibera'
import { SSE_EVENT, isActive, normalizeRounds } from '#shared/types/delibera'
import { useDebates } from './useDebates'

const POLL_INTERVAL_MS = 3000

export function useDebateStream() {
  const rounds = ref<DebateRoundDto[]>([])
  const status = ref<string | null>(null)
  const errorMessage = ref<string | null>(null)
  const connected = ref(false)
  /** True when the live stream failed and the UI has fallen back to polling. */
  const polling = ref(false)

  let source: EventSource | null = null
  let pollTimer: ReturnType<typeof setInterval> | null = null
  let onTerminal: (() => void) | null = null

  function stopPolling(): void {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  function close(): void {
    source?.close()
    source = null
    stopPolling()
    connected.value = false
  }

  /**
   * Terminal events do not carry the full DebateResponse — no token stats, no voting, no
   * duration. The authoritative record is re-fetched on arrival.
   */
  function finish(): void {
    close()
    onTerminal?.()
  }

  function handleCompleted(event: MessageEvent): void {
    const data = safeParse<DebateCompletedEvent>(event.data)
    status.value = data?.status ?? 'Completed'
    if (data?.error) errorMessage.value = data.error
    finish()
  }

  function handleError(event: MessageEvent): void {
    const data = safeParse<DebateErrorEvent>(event.data)
    status.value = data?.status ?? 'Failed'
    errorMessage.value =
      data?.error ?? 'The stream ended before a terminal event was received.'
    finish()
  }

  function handleCancelled(event: MessageEvent): void {
    // The server sends only `{ debateId }` here (SseDebateStreamWriter.cs:72), with no
    // status field — the outcome is known by definition, so it is asserted rather than read.
    safeParse<DebateCancelledEvent>(event.data)
    status.value = 'Cancelled'
    finish()
  }

  function handleRound(event: MessageEvent): void {
    // The polymorphic payload: a single round while live, an array on reconnect to an
    // already-Completed debate. normalizeRounds handles both.
    const incoming = normalizeRounds(safeParse(event.data))
    for (const round of incoming) {
      // Guard against a replayed round appending a duplicate after a reconnect.
      if (rounds.value.some((existing) => existing.roundNumber === round.roundNumber)) {
        continue
      }
      rounds.value.push(round)
    }
    rounds.value.sort((a, b) => a.roundNumber - b.roundNumber)
  }

  function safeParse<T = unknown>(raw: string): T | null {
    try {
      return JSON.parse(raw) as T
    } catch {
      return null
    }
  }

  /** Polls the record endpoint until the debate leaves an active state. */
  function startPolling(id: string): void {
    if (pollTimer) return
    polling.value = true

    const { get } = useDebates()
    pollTimer = setInterval(async () => {
      try {
        const record: DebateResponse = await get(id)
        status.value = record.status
        if (record.errorMessage) errorMessage.value = record.errorMessage
        if (!isActive(record.status)) finish()
      } catch (error) {
        // 404 is terminal, not transient: the record is either unknown or past its ~30 min
        // eviction, and polling it forever just hammers the API with a request that can
        // never succeed. Any other failure (5xx, unreachable) keeps polling, because the
        // debate may still be running elsewhere after a restart.
        if ((error as { status?: number })?.status === 404) {
          stopPolling()
          return
        }
      }
    }, POLL_INTERVAL_MS)
  }

  function start(id: string, onComplete?: () => void): void {
    close()
    onTerminal = onComplete ?? null
    const { streamUrl } = useDebates()

    if (typeof EventSource === 'undefined') {
      startPolling(id)
      return
    }

    source = new EventSource(streamUrl(id))

    source.addEventListener(SSE_EVENT.round, handleRound as EventListener)
    source.addEventListener(SSE_EVENT.completed, handleCompleted as EventListener)
    source.addEventListener(SSE_EVENT.error, handleError as EventListener)
    source.addEventListener(SSE_EVENT.cancelled, handleCancelled as EventListener)

    source.onopen = () => {
      connected.value = true
      polling.value = false
      stopPolling()
    }

    // Fires on a transport failure OR a server-sent `debate-error`; the explicit listener
    // above is registered first, so a payload-carrying error is already handled by the
    // time this runs. Anything still open here is a real connection problem.
    source.onerror = () => {
      connected.value = false
      if (source?.readyState === EventSource.CLOSED) {
        close()
        startPolling(id)
      }
    }
  }

  onScopeDispose(close)

  return { rounds, status, errorMessage, connected, polling, start, close }
}