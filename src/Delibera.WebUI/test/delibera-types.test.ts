import { describe, expect, it } from 'vitest'
import {
  normalizeRounds,
  isActive,
  isTerminal,
  type DebateRoundDto,
} from '#shared/types/delibera'

/**
 * `debate-round` carries a single round while a debate is live
 * (SseDebateStreamWriter.cs:142) but a JSON ARRAY when a client reconnects to an
 * already-Completed debate (:59). This is the single most likely source of a WebUI bug, so
 * it is pinned here rather than discovered at runtime.
 */
describe('normalizeRounds', () => {
  const round: DebateRoundDto = {
    roundNumber: 1,
    isFinal: false,
    strategy: 'StandardDebateStrategy',
    messages: [],
  }

  it('wraps a single round object into an array', () => {
    expect(normalizeRounds(round)).toEqual([round])
  })

  it('passes an array through unchanged', () => {
    expect(normalizeRounds([round, { ...round, roundNumber: 2 }])).toHaveLength(2)
  })

  it('treats an empty array as no rounds', () => {
    expect(normalizeRounds([])).toEqual([])
  })

  it('returns nothing for null, undefined and primitives', () => {
    // A malformed frame must not crash the timeline; it should simply contribute nothing.
    expect(normalizeRounds(null)).toEqual([])
    expect(normalizeRounds(undefined)).toEqual([])
    expect(normalizeRounds('debate-round')).toEqual([])
    expect(normalizeRounds(42)).toEqual([])
  })
})

describe('status helpers', () => {
  it('treats Pending and Running as active', () => {
    expect(isActive('Pending')).toBe(true)
    expect(isActive('Running')).toBe(true)
  })

  it('treats terminal states as not active', () => {
    expect(isActive('Completed')).toBe(false)
    expect(isActive('Failed')).toBe(false)
    expect(isActive('Cancelled')).toBe(false)
  })

  it('classifies the three terminal states', () => {
    expect(isTerminal('Completed')).toBe(true)
    expect(isTerminal('Failed')).toBe(true)
    expect(isTerminal('Cancelled')).toBe(true)
    expect(isTerminal('Running')).toBe(false)
  })

  it('treats an absent status as neither active nor terminal', () => {
    // Guard against truthiness bugs: a missing status must not read as "still running",
    // which would keep a poll loop alive forever.
    expect(isActive(undefined)).toBe(false)
    expect(isTerminal(null)).toBe(false)
  })
})