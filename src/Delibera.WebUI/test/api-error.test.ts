import { describe, expect, it } from 'vitest'
import { parseApiError, toCamelCaseKey } from '../app/composables/useApiError'

function response(
  status: number,
  body: string,
  contentType?: string,
  headers: Record<string, string> = {},
): Response {
  return new Response(body, {
    status,
    headers: {
      ...(contentType ? { 'Content-Type': contentType } : {}),
      ...headers,
    },
  })
}

const JSON_TYPE = 'application/problem+json; charset=utf-8'
const TEXT_TYPE = 'text/plain; charset=utf-8'

/**
 * Delibera.Server does not return one error shape. A single `response.json()` path breaks
 * on two of the cases below, so the parser is content-type aware by necessity.
 */
describe('parseApiError', () => {
  it('parses a JSON ValidationProblemDetails with PascalCase keys', async () => {
    // ValidationFilter -> Results.ValidationProblem, 400.
    const body = JSON.stringify({
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: {
        Question: 'Question must be between 1 and 4096 characters.',
        'Options.MaxRounds': "'Options.MaxRounds' must be between 1 and 20.",
      },
    })

    const error = await parseApiError(response(400, body, JSON_TYPE))

    expect(error.status).toBe(400)
    expect(error.fieldErrors['question']).toEqual([
      'Question must be between 1 and 4096 characters.',
    ])
    expect(error.fieldErrors['options.maxRounds']).toHaveLength(1)
  })

  it('parses a JSON ProblemDetails and surfaces the correlation extension', async () => {
    // ProblemDetailsExceptionHandler adds `correlationId`, the only link to a server log.
    const body = JSON.stringify({
      title: 'An unexpected error occurred.',
      status: 500,
      detail: 'Boom',
      correlationId: 'abc123',
    })

    const error = await parseApiError(response(500, body, JSON_TYPE))

    expect(error.message).toBe('An unexpected error occurred.')
    expect(error.detail).toBe('Boom')
    expect(error.correlationId).toBe('abc123')
  })

  it('reads the correlation id from the response header when the body lacks it', async () => {
    const body = JSON.stringify({ title: 'Oops', status: 500 })
    const error = await parseApiError(
      response(500, body, JSON_TYPE, { 'X-Correlation-Id': 'from-header' }),
    )
    expect(error.correlationId).toBe('from-header')
  })

  it('keeps the server message from a text/plain 409', async () => {
    // Conflict<string> from GET /debates/{id}/result is text/plain.
    const error = await parseApiError(
      response(409, 'Debate is Running, not yet completed.', TEXT_TYPE),
    )

    expect(error.status).toBe(409)
    expect(error.message).toBe('Debate is Running, not yet completed.')
    expect(error.fieldErrors).toEqual({})
  })

  it('replaces the default status-code page text with a readable message', async () => {
    // UseStatusCodePages() is registered with no formatter, so a bare 404 is text/plain
    // boilerplate rather than anything useful.
    const error = await parseApiError(
      response(404, 'Status Code: 404; Not Found', TEXT_TYPE),
    )

    expect(error.status).toBe(404)
    expect(error.message).toBe('Not found.')
  })

  it('handles an empty body', async () => {
    const error = await parseApiError(response(500, '', TEXT_TYPE))
    expect(error.status).toBe(500)
    expect(error.message).toBeTruthy()
  })

  it('does not throw on malformed JSON', async () => {
    // An unreadable body must still yield a usable error; otherwise a network hiccup
    // surfaces as "Cannot read properties of undefined".
    const error = await parseApiError(response(500, '{not json', JSON_TYPE))
    expect(error.status).toBe(500)
    expect(error.detail).toContain('{not json')
  })

  it('falls back to a status-specific message when the body has no title', async () => {
    const error = await parseApiError(response(400, '{}', JSON_TYPE))
    expect(error.message).toBe('The request was rejected as invalid.')
  })
})

describe('toCamelCaseKey', () => {
  it('lowercases each dotted segment', () => {
    expect(toCamelCaseKey('Options.MaxRounds')).toBe('options.maxRounds')
    expect(toCamelCaseKey('Question')).toBe('question')
    expect(toCamelCaseKey('TemplateId')).toBe('templateId')
  })

  it('leaves an already-camel key alone', () => {
    expect(toCamelCaseKey('templateId')).toBe('templateId')
  })
})