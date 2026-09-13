import type { RoundEntryInput, RoundSummary } from './types'

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

  /**
   * Scored nothing this round. Keeps its slot, so nobody behind is promoted.
   *
   * Covers a rule break and a team that never finished alike. They were
   * separate controls for one release and it was not worth it: both score the
   * same, and a second near-identical button on every row was one more thing to
   * read under time pressure.
   */
  dq: string[]

  bonus: Record<string, number>
  multiplier: number
  gameId: string | null

  /**
   * True while the next taps should join one shared place.
   *
   * A tie is announced as often as it is noticed. "Blue and Yellow tied for
   * second" is a sentence a scorekeeper hears before touching anything, and
   * arming a tie then tapping both maps onto it directly. A tie realized after
   * the fact is undo, arm, retap.
   */
  tieArmed: boolean
  /** Which group the armed taps are collecting into, once the first has landed. */
  tieInto: number | null

  past: Snapshot[]
}

interface Snapshot {
  groups: string[][]
  dq: string[]
  bonus: Record<string, number>
  /** Restored too, so undoing inside a tie leaves it still collecting. */
  tieInto: number | null
}

export type DraftAction =
  | { type: 'tapTeam'; teamId: string }
  | { type: 'toggleTieArm' }
  | { type: 'toggleDq'; teamId: string }
  | { type: 'setBonus'; teamId: string; points: number }
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
    bonus: { ...state.bonus },
    tieInto: state.tieInto,
  }
}

/**
 * Applies a change and records the previous state so it can be stepped back.
 *
 * Only round CONTENT goes on the stack. The game and the multiplier are
 * settings, visible on screen and one click to change, and putting them here
 * made undo step through those instead of the taps it exists to reverse.
 */
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

/**
 * The finishing slot each group occupies, from 1.
 *
 * NOT the group's position in the list. Two teams sharing first consume slots 1
 * and 2, so the next group is third. Everything the scorekeeper sees, from the
 * chip on a team block to the row label, has to agree with this, because it is
 * what the engine records.
 */
export function startSlots(state: DraftState): number[] {
  let slot = 1
  return state.groups.map((group) => {
    const start = slot
    slot += group.length
    return start
  })
}

/** The place shown for one team, or null when it has not been placed. */
export function slotOf(state: DraftState, teamId: string): number | null {
  const slots = startSlots(state)
  const index = state.groups.findIndex((group) => group.includes(teamId))
  return index === -1 ? null : slots[index]
}

/** Drops a team from wherever it is, discarding any group left empty. */
function withoutTeam(groups: string[][], teamId: string): string[][] {
  return groups.map((group) => group.filter((id) => id !== teamId)).filter((group) => group.length > 0)
}

export function roundDraftReducer(state: DraftState, action: DraftAction): DraftState {
  switch (action.type) {
    case 'tapTeam': {
      // A game has to be chosen before a round can be built, so there is never
      // a round sitting in an unsendable state for a reason that is off screen.
      if (!state.gameId) return state

      // Tapping a team that is already down is a no-op rather than an error.
      // Under time pressure a double tap is a slip, not an instruction.
      if (isPlaced(state, action.teamId)) return state

      if (state.tieArmed) {
        // Open the shared place, or join the one already open. An index that no
        // longer points at a group opens a new one rather than matching nothing
        // and dropping the tap on the floor, which is the one outcome here that
        // would cost a round without saying so.
        const collecting =
          state.tieInto !== null && state.tieInto >= 0 && state.tieInto < state.groups.length

        if (!collecting) {
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
      // Not on the undo stack: arming changes nothing about the round, and
      // stepping back through mode changes makes undo useless for mistakes.
      return { ...state, tieArmed: !state.tieArmed, tieInto: null }

    case 'toggleDq': {
      const on = state.dq.includes(action.teamId)

      return commit(state, {
        dq: on ? state.dq.filter((id) => id !== action.teamId) : [...state.dq, action.teamId],
      })
    }

    case 'setBonus': {
      const bonus = { ...state.bonus }
      if (action.points <= 0) delete bonus[action.teamId]
      else bonus[action.teamId] = action.points

      return commit(state, { bonus })
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
      // A setting, not round content. See commit().
      return { ...state, gameId: action.gameId }

    case 'setMultiplier':
      return { ...state, multiplier: action.multiplier }

    case 'undo': {
      if (state.past.length === 0) return state

      const past = [...state.past]
      const previous = past.pop()!

      return {
        ...state,
        ...previous,
        past,
        // Whatever the tie was collecting into at the time, which is null once
        // stepped back past the tap that started the group.
        tieInto: previous.tieInto ?? null,
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
  if (!state.gameId) return 'Pick a game to start'
  if (state.tieArmed) return 'Finish the tie first'
  if (state.groups.length === 0) return 'Tap a team to start'

  const placed = new Set(placedTeams(state))
  const missing = allTeamIds.filter((id) => !placed.has(id))

  if (missing.length === 1) return 'Waiting on the last team'
  if (missing.length > 1) return `Waiting on ${missing.length} teams`

  return null
}

/** Turns the draft into the shape the API takes. */
export function toEntries(state: DraftState): RoundEntryInput[] {
  const slots = startSlots(state)

  return state.groups.flatMap((group, index) =>
    group.map((teamId) => ({
      teamId,
      place: slots[index],
      isDisqualified: state.dq.includes(teamId),
      bonus: state.bonus[teamId] ?? 0,
      bonusReason: state.bonus[teamId] ? 'Bonus' : null,
    })),
  )
}

/**
 * Rebuilds the draft that would produce an already recorded round.
 *
 * This is what makes a round editable. The correction is saved back over the
 * same round, so it keeps its id and its place in the night, and the usual
 * reason for one is a single wrong place out of four. Starting from what is
 * already recorded means fixing that one place rather than retyping the round.
 */
export function fromRound(round: RoundSummary): DraftState {
  const placed = round.teams
    .filter((team) => team.place !== null)
    .sort((a, b) => a.place! - b.place!)

  const groups: string[][] = []
  let previous: number | null = null

  for (const team of placed) {
    // Teams sharing a place tied, which is how the group was stored.
    if (team.place === previous) groups[groups.length - 1].push(team.teamId)
    else groups.push([team.teamId])

    previous = team.place
  }

  const bonus: Record<string, number> = {}
  for (const team of round.teams) {
    if (team.bonusPoints > 0) bonus[team.teamId] = team.bonusPoints
  }

  return {
    ...emptyDraft(round.gameId),
    groups,
    dq: round.teams.filter((team) => team.isDisqualified).map((team) => team.teamId),
    bonus,
    multiplier: round.multiplier,
  }
}
