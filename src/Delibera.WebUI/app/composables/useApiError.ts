/**
 * Content-type-aware error parsing.
 *
 * Delibera.Server does not return one error shape, and a single `response.json()` path
 * breaks on two of the four cases:
 *
 *   - 400 validation -> JSON ValidationProblemDetails, but with PASCALCASE field keys
 *     ("Question", "Options.MaxRounds") and one joined string per field.
 *   - 409 from /debates/{id}/result -> `Conflict<string>` -> text/plain.
 *   - bare 404 -> `UseStatusCodePages()` registered with no formatter -> text/plain
 *     ("Status Code: 404; Not Found").
 *   - 5xx -> JSON ProblemDetails via ProblemDetailsExceptionHandler.
 */

export interface ParsedApiError {
  status: number
  /** Human-readable summary suitable for display. */
  message: string
  /** RFC 7807 detail, when the server supplied one. */
  detail?: string
  /** Field name -> messages, keys normalised to camelCase for form binding. */
  fieldErrors: Record<string, string[]>
  correlationId?: string
}

interface ValidationProblemDetails {
  errors?: Record<string, string[] | string>
  detail?: string
  title?: string
}

interface ProblemDetails {
  title?: string
  detail?: string
  correlationId?: string
}

/** `"Options.MaxRounds"` -> `"options.maxRounds"`, so v-model bindings line up. */
export function toCamelCaseKey(key: string): string {
  return key
    .split('.')
    .map((part) => part.charAt(0).toLowerCase() + part.slice(1))
    .join('.')
}

function extractFieldErrors(body: ValidationProblemDetails): Record<string, string[]> {
  const result: Record<string, string[]> = {}
  for (const [key, value] of Object.entries(body.errors ?? {})) {
    const messages = Array.isArray(value) ? value : [value]
    result[toCamelCaseKey(key)] = messages
  }
  return result
}

function defaultMessageFor(status: number): string {
  switch (status) {
    case 400:
      return 'The request was rejected as invalid.'
    case 404:
      return 'Not found.'
    case 409:
      return 'The debate is not in a state that allows this.'
    case 502:
      return 'The Delibera API is unreachable.'
    case 503:
      return 'The Delibera API is unavailable.'
    default:
      return status >= 500 ? 'The server failed to handle the request.' : 'Request failed.'
  }
}

/**
 * Parses a non-2xx response.
 *
 * Never throws: an unreadable body must still yield a usable error, or a network hiccup
 * surfaces as "Cannot read properties of undefined" instead of the real status.
 */
export async function parseApiError(response: Response): Promise<ParsedApiError> {
  const status = response.status
  const correlationId = response.headers.get('X-Correlation-Id') ?? undefined
  const contentType = response.headers.get('Content-Type') ?? ''

  let raw = ''
  try {
    raw = await response.text()
  } catch {
    return { status, message: defaultMessageFor(status), fieldErrors: {}, correlationId }
  }

  const looksJson = contentType.includes('json')

  if (looksJson && raw.trim().length > 0) {
    let parsed: ValidationProblemDetails & ProblemDetails
    try {
      parsed = JSON.parse(raw)
    } catch {
      return {
        status,
        message: defaultMessageFor(status),
        detail: raw.slice(0, 500),
        fieldErrors: {},
        correlationId,
      }
    }

    const fieldErrors = extractFieldErrors(parsed)

    return {
      status,
      message: parsed.title ?? defaultMessageFor(status),
      detail: parsed.detail,
      fieldErrors,
      // ProblemDetailsExceptionHandler writes this extension; it is the only way to tie a
      // user-visible error back to a server log line.
      correlationId: parsed.correlationId ?? correlationId,
    }
  }

  // text/plain. The status-code default writer produces "Status Code: 404; Not Found",
  // which is noise; the server's own messages ("Debate is Running, not yet completed.")
  // are the useful part.
  const text = raw.trim()
  const isDefaultStatusPage =
    /^Status Code: \d+/.test(text) || text.length === 0 || /^\s*Not Found\s*$/i.test(text)

  return {
    status,
    message: isDefaultStatusPage ? defaultMessageFor(status) : text.slice(0, 500),
    fieldErrors: {},
    correlationId,
  }
}

/** Human-readable status text for a debate in a non-terminal state. */
export function describeStatus(status: string | undefined | null): string {
  switch (status) {
    case 'Pending':
      return 'Queued'
    case 'Running':
      return 'In progress'
    case 'Completed':
      return 'Completed'
    case 'Failed':
      return 'Failed'
    case 'Cancelled':
      return 'Cancelled'
    default:
      return status ?? 'Unknown'
  }
}