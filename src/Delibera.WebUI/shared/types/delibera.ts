/**
 * TypeScript mirror of the Delibera.Server REST contracts.
 *
 * Source of truth:
 *   src/Delibera.Server/Api/Contracts/*.cs
 *   src/Delibera.Server/Api/Mapping/DebateMapper.cs
 *
 * Three server behaviours shape every type below, and each one has bitten a client before:
 *
 *  1. `DebateStatus` serialises as a STRING (JsonStringEnumConverter), never a number.
 *  2. `DefaultIgnoreCondition = WhenWritingNull` — a null field is ABSENT from the response,
 *     not present-and-null. Optional fields are therefore typed `| null` and must be treated
 *     as possibly-missing; `undefined` and `null` both mean "server did not send this".
 *  3. Nulls aside, every property is camelCase.
 */

/** `DebateResponse.cs:35` — the only enum in the REST surface. */
export const DEBATE_STATUSES = [
  'Pending',
  'Running',
  'Completed',
  'Failed',
  'Cancelled',
] as const

export type DebateStatus = (typeof DEBATE_STATUSES)[number]

/** Statuses for which a debate may still change. */
export const ACTIVE_STATUSES: readonly DebateStatus[] = ['Pending', 'Running']

/** Statuses after which no further SSE events will arrive. */
export const TERMINAL_STATUSES: readonly DebateStatus[] = [
  'Completed',
  'Failed',
  'Cancelled',
]

export function isActive(status: DebateStatus | undefined | null): boolean {
  return !!status && ACTIVE_STATUSES.includes(status)
}

export function isTerminal(status: DebateStatus | undefined | null): boolean {
  return !!status && TERMINAL_STATUSES.includes(status)
}

/** `VerdictDto.cs` */
export interface RiskItemDto {
  category: string
  description: string
  severity: string
  mitigation?: string | null
}

/**
 * IMPORTANT — this is mostly a shell.
 *
 * `DebateMapper.MapVerdict` populates ONLY `recommendation` and `rawJson`. The other fields
 * exist on the C# record but are never filled in, so a UI built around `confidence` or
 * `risks` renders permanently blank. The real structured output lives inside `rawJson`
 * and is template-specific — render it as formatted JSON, not as typed fields.
 */
export interface VerdictDto {
  recommendation?: string | null
  riskLevel?: string | null
  rationale?: string | null
  confidence?: number | null
  conditions?: string[] | null
  risks?: RiskItemDto[] | null
  /** `JsonSerializer.Serialize(result.TypedVerdict)` — shape depends on the template. */
  rawJson?: string | null
}

export interface VoteTallyDto {
  candidate: string
  votes: number
  score: number
}

/**
 * `Tally[].votes` is 1 for the top entry and 0 for every other entry, and `totalVotes`
 * counts scored options rather than summing them (`DebateMapper.cs:57-60`). Treat the tally
 * as an ordering, not as vote arithmetic.
 */
export interface VotingResultDto {
  strategy: string
  winner: string
  totalVotes: number
  tally: VoteTallyDto[]
}

export interface TokenStatsDto {
  totalInputTokens: number
  totalOutputTokens: number
  totalTokens: number
  savedByCompression: number
}

export interface DebateResponse {
  debateId: string
  templateId: string
  tenantId: string
  status: DebateStatus
  finalVerdict?: string | null
  verdict?: VerdictDto | null
  voting?: VotingResultDto | null
  tokenStats?: TokenStatsDto | null
  errorMessage?: string | null
  cacheHit?: boolean | null
  cacheKey?: string | null
  durationMs?: number | null
  label?: string | null
  /**
   * Absolute, built from Scheme://Host with no forwarded-header handling (`DebateMapper.cs:9`).
   * Wrong behind a proxy. Compose your own URLs from the same-origin proxy route instead.
   */
  streamUrl: string
  /** Same caveat as `streamUrl`. */
  resultUrl: string
  createdAt: string
  completedAt?: string | null
}

export interface ParticipantMessageDto {
  role: string
  content: string
  /** Always `string.Empty` server-side (`DebateMapper.cs:94`), so always present-and-blank. */
  modelName?: string | null
}

export interface OperatorInteractionDto {
  task: string
  result: string
}

export interface DebateRoundDto {
  roundNumber: number
  isFinal: boolean
  /** A CLR type name such as `StandardDebateStrategy`, or `""` (`DebateMapper.cs:84`). */
  strategy: string
  messages: ParticipantMessageDto[]
  chairmanSummary?: string | null
  operatorInteractions?: OperatorInteractionDto[] | null
}

export interface TemplateDto {
  templateId: string
  displayName: string
  description?: string | null
  strategy: string
  defaultMaxRounds: number
  memberRoles: string[]
  votingStrategy: string
  ragEnabled: boolean
  operatorEnabled: boolean
}

export interface DebateOptionsOverride {
  /** 1–20 inclusive when present. */
  maxRounds?: number | null
  /** 0–2 inclusive when present. */
  temperature?: number | null
  compressionStrategy?: string | null
  votingStrategy?: string | null
  streamingEnabled?: boolean | null
  memberWeights?: Record<string, number> | null
}

export interface CreateDebateRequest {
  templateId: string
  /** 1–4096 characters. */
  question: string
  inputData?: unknown
  corpusIds?: string[] | null
  knowledgeText?: string | null
  options?: DebateOptionsOverride | null
}

/** Server-side validation limits — `Api/Validators/CreateDebateRequestValidator.cs`. */
export const LIMITS = {
  questionMin: 1,
  questionMax: 4096,
  maxRoundsMin: 1,
  maxRoundsMax: 20,
  temperatureMin: 0,
  temperatureMax: 2,
} as const

/**
 * SSE terminal payloads (`SseDebateStreamWriter.cs:266-286`). Note `verdict` here is the raw
 * FinalVerdict string, NOT a VerdictDto.
 */
export interface DebateCompletedEvent {
  debateId: string
  status: DebateStatus
  verdict?: string | null
  completedAt?: string | null
  error?: string | null
}

export interface DebateErrorEvent {
  debateId: string
  status: DebateStatus
  error?: string | null
}

export interface DebateCancelledEvent {
  debateId: string
}

export const SSE_EVENT = {
  round: 'debate-round',
  completed: 'debate-completed',
  error: 'debate-error',
  cancelled: 'debate-cancelled',
} as const

/**
 * `debate-round` is polymorphic: a single `DebateRoundDto` on the live path
 * (`SseDebateStreamWriter.cs:142`) but a JSON ARRAY when a client reconnects to an
 * already-Completed debate (`:59`). Normalising here is the single most important
 * correctness detail in the whole client.
 */
export function normalizeRounds(payload: unknown): DebateRoundDto[] {
  if (Array.isArray(payload)) return payload as DebateRoundDto[]
  if (payload && typeof payload === 'object') return [payload as DebateRoundDto]
  return []
}