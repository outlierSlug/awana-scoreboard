import type { RoundEntryInput } from './types'

/**
 * The in-progress round, before it is confirmed.
 *
 * This is the one genuinely complex piece of client state in the product, and
 * it is deliberately a pure reducer rather than a pile of useState hooks. The
 * previous version scattered it across six pieces of interdependent state with
 * nowhere to test the invariants, and that is exactly where its interaction
 * bugs lived.
 */
export interface DraftState {
  /**
   * Finish order as ordered groups. Teams in the same group tied, which maps
   * straight onto the Place integers the API expects.
   */
  groups: string[][]
  dq: string[]
  absent: string[]
  bonus: Record<string, number>
  multiplier: number
  gameId: string | null

  /**
   * True while the next taps should join one shared place.
   *
   * This exists because a tie is announced as often as it is noticed. "Blue and
   * Yellow tied for second" is a sentence a scorekeeper hears before touching
   * anything, and arming a tie then tapping both maps onto it directly. Ties
   * realized after the fact are handled by tieUp on the row instead, and both
   * routes end in the same state.
   */
  tieArmed: boolean
  /** Which group the armed taps are collecting into, once the first has landed. */
  tieInto: number | null

  past: Snapshot[]
}

interface Snapshot {
  groups: string[][]
  dq: string[]
  absent: string[]
  bonus: Record<string, number>
  multiplier: number
  gameId: string | null
}

export type DraftAction =
  | { type: 'tapTeam'; teamId: string }
  | { type: 'toggleTieArm' }
  | { type: 'tieUp'; groupIndex: number }
  | { type: 'move'; groupIndex: number; direction: 'up' | 'down' }
  | { type: 'toggleDq'; teamId: string }
  | { type: 'setBonus'; teamId: string; points: number }
  | { type: 'toggleAbsent'; teamId: string }
  | { type: 'remove'; teamId: string }
  | { type: 'setGame'; gameId: string }
  | { type: 'setMultiplier'; multiplier: number }
  | { type: 'undo' }
  | { type: 'reset' }
  | { type: 'restore'; state: DraftState }

/** Deep enough for a scorekeeper to tap their way out of any mess. */
const UNDO_LIMIT = 20

export function emptyDraft(gameId: string | null = null): DraftState {
  return {
    groups: [],
    dq: [],
    absent: [],
    bonus: {},
    multiplier: 1,
    gameId,
    tieArmed: false,
    tieInto: null,
    past: [],
  }
}

function snapshot(state: DraftState): Snapshot {
  return {
    groups: state.groups.map((group) => [...group]),
    dq: [...state.dq],
    absent: [...state.absent],
    bonus: { ...state.bonus },
    multiplier: state.multiplier,
    gameId: state.gameId,
  }
}

/** Applies a change and records the previous state so it can be stepped back. */
function commit(state: DraftState, next: Partial<DraftState>): DraftState {
  return {
    ...state,
    ...next,
    past: [...state.past, snapshot(state)].slice(-UNDO_LIMIT),
  }
}

export function placedTeams(state: DraftState): string[] {
  return state.groups.flat()
}

export function isPlaced(state: DraftState, teamId: string): boolean {
  return state.groups.some((group) => group.includes(teamId))
}

/** Drops a team from wherever it is, discarding any group left empty. */
function withoutTeam(groups: string[][], teamId: string): string[][] {
  return groups.map((group) => group.filter((id) => id !== teamId)).filter((group) => group.length > 0)
}

export function roundDraftReducer(state: DraftState, action: DraftAction): DraftState {
  switch (action.type) {
    case 'tapTeam': {
      // Tapping a team that is already down is a no-op rather than an error.
      // Under time pressure a double tap is a slip, not an instruction.
      if (isPlaced(state, action.teamId) || state.absent.includes(action.teamId)) return state

      if (state.tieArmed) {
        if (state.tieInto === null) {
          return commit(state, {
            groups: [...state.groups, [action.teamId]],
            tieInto: state.groups.length,
          })
        }

        const groups = state.groups.map((group, index) =>
          index === state.tieInto ? [...group, action.teamId] : group,
        )
        return commit(state, { groups })
      }

      return commit(state, { groups: [...state.groups, [action.teamId]] })
    }

    case 'toggleTieArm':
      // Not committed to the undo stack: arming changes nothing about the round,
      // and having undo step through mode changes makes it useless for mistakes.
      return { ...state, tieArmed: !state.tieArmed, tieInto: null }

    case 'tieUp': {
      // Merge this group into the one above it.
      if (action.groupIndex <= 0 || action.groupIndex >= state.groups.length) return state

      const groups = [...state.groups]
      const merged = [...groups[action.groupIndex - 1], ...groups[action.groupIndex]]
      groups.splice(action.groupIndex - 1, 2, merged)

      return commit(state, { groups, tieInto: null })
    }

    case 'move': {
      const target = action.direction === 'up' ? action.groupIndex - 1 : action.groupIndex + 1
      if (target < 0 || target >= state.groups.length) return state

      const groups = [...state.groups]
      ;[groups[action.groupIndex], groups[target]] = [groups[target], groups[action.groupIndex]]

      return commit(state, { groups, tieInto: null })
    }

    case 'toggleDq': {
      const dq = state.dq.includes(action.teamId)
        ? state.dq.filter((id) => id !== action.teamId)
        : [...state.dq, action.teamId]

      return commit(state, { dq })
    }

    case 'setBonus': {
      const bonus = { ...state.bonus }
      if (action.points === 0) delete bonus[action.teamId]
      else bonus[action.teamId] = action.points

      return commit(state, { bonus })
    }

    case 'toggleAbsent': {
      if (state.absent.includes(action.teamId)) {
        return commit(state, { absent: state.absent.filter((id) => id !== action.teamId) })
      }

      // Marking a team absent also takes it out of the finish order, so the two
      // can never disagree about whether it took part.
      return commit(state, {
        absent: [...state.absent, action.teamId],
        groups: withoutTeam(state.groups, action.teamId),
        dq: state.dq.filter((id) => id !== action.teamId),
        tieInto: null,
      })
    }

    case 'remove': {
      const bonus = { ...state.bonus }
      delete bonus[action.teamId]

      return commit(state, {
        groups: withoutTeam(state.groups, action.teamId),
        dq: state.dq.filter((id) => id !== action.teamId),
        bonus,
        tieInto: null,
      })
    }

    case 'setGame':
      return commit(state, { gameId: action.gameId })

    case 'setMultiplier':
      return commit(state, { multiplier: action.multiplier })

    case 'undo': {
      if (state.past.length === 0) return state

      const past = [...state.past]
      const previous = past.pop()!

      return {
        ...state,
        ...previous,
        past,
        // The collecting group may no longer exist after stepping back.
        tieInto: null,
      }
    }

    case 'reset':
      // Keeps the game and multiplier: clearing a mistake should not also
      // un-pick the game that is still being played.
      return { ...emptyDraft(state.gameId), multiplier: state.multiplier }

    case 'restore':
      return action.state

    default:
      return state
  }
}

/**
 * Why this round cannot be sent yet, or null when it can.
 *
 * Returned as a sentence rather than a boolean so the confirm button can say
 * what it is waiting for. A button that is simply greyed out with no reason is
 * the thing that makes people jab at a screen.
 */
export function blockingReason(state: DraftState, allTeamIds: string[]): string | null {
  if (!state.gameId) return 'Pick a game'
  if (state.groups.length === 0) return 'Tap a team to start'

  const accountedFor = new Set([...placedTeams(state), ...state.absent])
  const missing = allTeamIds.filter((id) => !accountedFor.has(id))

  if (missing.length > 0) return 'Waiting on the last team'
  return null
}

/** Turns the draft into the shape the API takes. */
export function toEntries(state: DraftState): RoundEntryInput[] {
  const entries: RoundEntryInput[] = []

  state.groups.forEach((group, index) => {
    for (const teamId of group) {
      entries.push({
        teamId,
        place: index + 1,
        isDisqualified: state.dq.includes(teamId),
        bonus: state.bonus[teamId] ?? 0,
        bonusReason: state.bonus[teamId] ? 'Bonus' : null,
      })
    }
  })

  for (const teamId of state.absent) {
    entries.push({
      teamId,
      place: null,
      isDisqualified: false,
      bonus: 0,
      bonusReason: null,
    })
  }

  return entries
}
