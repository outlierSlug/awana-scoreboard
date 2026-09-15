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

/** Points a leader decided on, outside the games. */
export interface Adjustment {
  id: string
  teamId: string
  teamName: string
  points: number
  reason: string
  isVoided: boolean
  createdAt: string
}

export interface CreateAdjustmentRequest {
  teamId: string
  /** Negative is a penalty. Never zero. */
  points: number
  reason: string
}

export interface TeamHeadcount {
  teamId: string
  headcount: number | null
}

export interface UpdateAttendanceRequest {
  teams: TeamHeadcount[]
}

/** Which rules a session scores under. Shown even when there was no choice. */
export interface SessionScoring {
  name: string
  placePoints: number[]
  /** Frozen onto the session. False while it is still in setup. */
  isFixed: boolean
  /** Nobody chose, so this is the church's default and can still move. */
  isDefault: boolean
}

export interface SessionDetail {
  id: string
  slug: string
  divisionId: string
  divisionName: string
  date: string
  status: SessionStatus
  version: number
  scoring: SessionScoring
  teams: SessionTeam[]
  rounds: RoundSummary[]
  adjustments: Adjustment[]
}

/** Roles, ordered so the UI can ask "at least this" the way the API does. */
export const UserRole = {
  Viewer: 'Viewer',
  Scorekeeper: 'Scorekeeper',
  GamesLeader: 'GamesLeader',
  Admin: 'Admin',
} as const

export type UserRole = (typeof UserRole)[keyof typeof UserRole]

const ROLE_RANK: Record<UserRole, number> = {
  Viewer: 10,
  Scorekeeper: 20,
  GamesLeader: 30,
  Admin: 40,
}

export function atLeast(role: string | null | undefined, minimum: UserRole): boolean {
  if (!role || !(role in ROLE_RANK)) return false
  return ROLE_RANK[role as UserRole] >= ROLE_RANK[minimum]
}

/** Who the caller is, or that they are nobody. Anonymous is a normal answer. */
export interface Me {
  isSignedIn: boolean
  userId: string | null
  displayName: string | null
  role: string | null
}

export interface Division {
  id: string
  name: string
  slug: string
}

/** What the round picker needs. The catalog page uses GameDetail instead. */
export interface Game {
  id: string
  name: string
}

/** A game as the catalog shows it, retired ones included. */
export interface GameDetail {
  id: string
  name: string
  notes: string | null
  /** Its place in one hand-arranged list. There are no tiers. */
  sortOrder: number
  isActive: boolean
  /**
   * A gate, not a statistic. Zero is what makes a game deletable; anything else
   * means retiring is the only option that keeps history readable. Not shown:
   * how often a game gets played belongs to week to week tracking.
   */
  roundCount: number
  /** Came with the app. Shown as provenance only: it is edited like any other. */
  isSeeded: boolean
}

// ------------------------------------------------------------- scoring rules

/** How tied teams divide the slots they consumed. Numeric, as the API sends. */
export const TieRule = {
  AverageSharedSlots: 0,
  HighestSlot: 1,
  LowestSlot: 2,
  NoTiesAllowed: 3,
} as const

export type TieRule = (typeof TieRule)[keyof typeof TieRule]

/** What happens to a disqualified team, and to the teams behind it. */
export const DqRule = {
  ZeroButHoldSlot: 0,
  DropToLast: 1,
  CustomPenalty: 2,
  ZeroAndPromoteOthers: 3,
} as const

export type DqRule = (typeof DqRule)[keyof typeof DqRule]

export const RoundingMode = {
  HalfAwayFromZero: 0,
  HalfToEven: 1,
} as const

export type RoundingMode = (typeof RoundingMode)[keyof typeof RoundingMode]

export interface RoundingSpec {
  decimals: number
  mode: RoundingMode
}

/**
 * Everything a church can configure about scoring.
 *
 * Mirrors Awana.Scoring.ScoringConfig field for field. The engine is the only
 * thing that executes these, so the shape is copied rather than reinterpreted.
 */
export interface ScoringConfig {
  /** Points per finishing slot, best first. The official default is 40/30/20/10. */
  placePoints: number[]
  /** What a team earns finishing past the end of the table. */
  pointsBeyondTable: number
  tieRule: TieRule
  dqRule: DqRule
  /** Used only when dqRule is CustomPenalty. */
  dqPenaltyPoints: number
  allowUnplacedTeams: boolean
  rounding: RoundingSpec
}

/** A named set of scoring rules. */
export interface ScoringProfile {
  id: string
  name: string
  config: ScoringConfig
  /** What a new session gets when nobody chooses. Exactly one is true. */
  isDefault: boolean
  isActive: boolean
  /**
   * A gate, not a statistic. Zero is what makes a profile deletable, because a
   * session that used it would otherwise lose the record of its scoring.
   */
  sessionCount: number
  /**
   * Came with the app, and is therefore read only even to an admin: the name is
   * a claim about a standard. Duplicate it to get a set you can change.
   */
  isSeeded: boolean
}

export interface SaveScoringProfileRequest {
  name: string
  config: ScoringConfig
}

/** What a set of rules would do, worked through by the real engine. */
export interface ScoringPreview {
  examples: ScoringExample[]
}

export interface ScoringExample {
  title: string
  question: string
  teams: ScoringExampleTeam[]
  /** Set when the rules refuse this shape rather than scoring it. */
  rejected: string | null
}

export interface ScoringExampleTeam {
  teamName: string
  place: number | null
  isDisqualified: boolean
  points: number
  explanation: string
}

/** The rules every church starts from, and what a new set is seeded with. */
export const DEFAULT_SCORING_CONFIG: ScoringConfig = {
  placePoints: [40, 30, 20, 10],
  pointsBeyondTable: 0,
  tieRule: TieRule.AverageSharedSlots,
  dqRule: DqRule.ZeroButHoldSlot,
  dqPenaltyPoints: 0,
  allowUnplacedTeams: true,
  rounding: { decimals: 0, mode: RoundingMode.HalfAwayFromZero },
}

/** The editable half of a game. The same shape creates one and updates one. */
export interface SaveGameRequest {
  name: string
  notes: string | null
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

/** The same shape as recording, minus the idempotency key: the round exists. */
export interface UpdateRoundRequest {
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
