/**
 * The API contract, mirrored.
 *
 * Hand-written rather than generated, because the surface is small and a
 * generator would be another build step to keep working three weeks before a
 * deadline. The names match the C# records in Awana.Api/Contracts exactly, so
 * a mismatch is a rename away from being obvious.
 */

export type SessionStatus = 0 | 1 | 2

export const SessionStatus = {
  Setup: 0,
  Running: 1,
  Finished: 2,
} as const

export interface Standing {
  teamId: string
  name: string
  colorHex: string
  textOnColorHex: string
  points: number
  rank: number
  /** Places gained since before the most recent round. Positive is upward. */
  rankChange: number
  placeCounts: Record<string, number>
}

export interface RoundTeam {
  teamId: string
  teamName: string
  place: number | null
  isDisqualified: boolean
  points: number
  bonusPoints: number
  explanation: string
}

export interface LastRound {
  roundId: string
  roundNumber: number
  gameName: string
  multiplier: number
  teams: RoundTeam[]
}

export interface Scoreboard {
  sessionId: string
  slug: string
  divisionName: string
  date: string
  status: SessionStatus
  /**
   * The session's logical clock. A message carrying a lower version than one
   * already applied is stale and must be discarded, which happens after a
   * reconnect when a queued push lands behind a fresher refetch.
   */
  version: number
  roundCount: number
  standings: Standing[]
  lastRound: LastRound | null
  serverTimeUtc: string
}

export interface SessionSummary {
  id: string
  slug: string
  divisionName: string
  date: string
  status: SessionStatus
  roundCount: number
}

export interface SessionTeam {
  teamId: string
  name: string
  colorHex: string
  textOnColorHex: string
  headcount: number | null
}

export interface RoundSummary {
  id: string
  roundNumber: number
  gameId: string
  gameName: string
  multiplier: number
  isVoided: boolean
  voidReason: string | null
  teams: RoundTeam[]
}

export interface SessionDetail {
  id: string
  slug: string
  divisionId: string
  divisionName: string
  date: string
  status: SessionStatus
  version: number
  teams: SessionTeam[]
  rounds: RoundSummary[]
}

export interface Division {
  id: string
  name: string
  slug: string
}

export interface Game {
  id: string
  name: string
  isCore: boolean
  divisionId: string | null
}

export interface RoundEntryInput {
  teamId: string
  place: number | null
  isDisqualified: boolean
  bonus: number
  bonusReason: string | null
}

export interface CreateRoundRequest {
  clientRequestId: string
  gameId: string
  multiplier: number
  entries: RoundEntryInput[]
}

export interface PreviewRequest {
  gameId: string
  multiplier: number
  entries: RoundEntryInput[]
}

export interface ValidationError {
  code: string
  message: string
}

export interface Preview {
  isValid: boolean
  errors: ValidationError[]
  awards: RoundTeam[]
}

export interface RoundRecorded {
  roundId: string
  roundNumber: number
  awards: RoundTeam[]
  scoreboard: Scoreboard
}
