import { describe, expect, it } from 'vitest'
import {
  blockingReason,
  emptyDraft,
  fromRound,
  placedTeams,
  roundDraftReducer as reduce,
  slotOf,
  startSlots,
  toEntries,
  type DraftAction,
  type DraftState,
} from './roundDraft'
import type { RoundSummary, RoundTeam } from './types'

const RED = 'red'
const BLUE = 'blue'
const YELLOW = 'yellow'
const GREEN = 'green'
const ALL = [RED, BLUE, YELLOW, GREEN]
const GAME = 'baton'

/** Applies a sequence, which is how these are actually used. */
function run(actions: DraftAction[], start: DraftState = emptyDraft(GAME)): DraftState {
  return actions.reduce(reduce, start)
}

const tap = (teamId: string): DraftAction => ({ type: 'tapTeam', teamId })

describe('tapping teams in finish order', () => {
  it('appends each team as its own place', () => {
    const state = run([tap(RED), tap(BLUE), tap(YELLOW)])

    expect(state.groups).toEqual([[RED], [BLUE], [YELLOW]])
  })

  it('ignores a team that is already placed', () => {
    // A double tap under time pressure is a slip, not an instruction.
    const state = run([tap(RED), tap(RED)])

    expect(state.groups).toEqual([[RED]])
  })

  it('does nothing until a game has been chosen', () => {
    // Otherwise the round sits unsendable for a reason that is off screen.
    const noGame = emptyDraft(null)
    const state = run([tap(RED), tap(BLUE)], noGame)

    expect(state.groups).toEqual([])
  })
})

describe('recording a tie', () => {
  it('arming collects every following tap into one place', () => {
    // "Blue and Yellow tied for second", recorded as it is said.
    const state = run([tap(RED), { type: 'toggleTieArm' }, tap(BLUE), tap(YELLOW)])

    expect(state.groups).toEqual([[RED], [BLUE, YELLOW]])
  })

  it('disarming ends the shared place', () => {
    const state = run([
      { type: 'toggleTieArm' },
      tap(RED),
      tap(BLUE),
      { type: 'toggleTieArm' },
      tap(YELLOW),
    ])

    expect(state.groups).toEqual([[RED, BLUE], [YELLOW]])
  })

  it('never drops a tap because the collecting place has gone', () => {
    // A stale index used to match no group at all, which placed nobody and
    // said nothing. Reachable or not, losing a tap is the one failure here
    // that costs a round.
    const stale: DraftState = {
      ...emptyDraft(GAME),
      groups: [[RED]],
      tieArmed: true,
      tieInto: 7,
    }

    const state = reduce(stale, tap(BLUE))

    expect(state.groups).toEqual([[RED], [BLUE]])
    expect(state.tieInto).toBe(1)
  })

  it('a tie spotted late is undo, arm, retap', () => {
    // The only recovery path now that the per-row merge button is gone, so it
    // has to reach the same state the armed route does.
    const late = run([
      tap(RED),
      tap(BLUE),
      { type: 'undo' },
      { type: 'undo' },
      { type: 'toggleTieArm' },
      tap(RED),
      tap(BLUE),
    ])
    const armed = run([{ type: 'toggleTieArm' }, tap(RED), tap(BLUE)])

    expect(late.groups).toEqual(armed.groups)
  })
})

describe('finishing places', () => {
  it('a tie consumes both slots, so the next team is third', () => {
    const state = run([{ type: 'toggleTieArm' }, tap(RED), tap(BLUE), { type: 'toggleTieArm' }, tap(YELLOW)])

    expect(startSlots(state)).toEqual([1, 3])
  })

  it('tied teams share the same place', () => {
    const state = run([tap(RED), { type: 'toggleTieArm' }, tap(BLUE), tap(YELLOW)])

    // The complaint this fixes: two teams tied for 2nd were numbered 2 and 3.
    expect(slotOf(state, BLUE)).toBe(2)
    expect(slotOf(state, YELLOW)).toBe(2)
  })

  it('reports no place for a team not yet tapped', () => {
    const state = run([tap(RED)])
    expect(slotOf(state, GREEN)).toBeNull()
  })

  it('sends the same places it shows', () => {
    const state = run([
      { type: 'toggleTieArm' },
      tap(RED),
      tap(BLUE),
      { type: 'toggleTieArm' },
      tap(YELLOW),
    ])

    const entries = toEntries(state)

    expect(entries.find((e) => e.teamId === RED)?.place).toBe(1)
    expect(entries.find((e) => e.teamId === BLUE)?.place).toBe(1)
    expect(entries.find((e) => e.teamId === YELLOW)?.place).toBe(3)
  })
})

describe('corrections', () => {
  it('toggles disqualification without changing the order', () => {
    const state = run([tap(RED), tap(BLUE), { type: 'toggleDq', teamId: RED }])

    expect(state.dq).toEqual([RED])
    expect(state.groups).toEqual([[RED], [BLUE]])

    expect(reduce(state, { type: 'toggleDq', teamId: RED }).dq).toEqual([])
  })

  it('a disqualified team keeps its slot, so nobody behind is promoted', () => {
    const state = run([tap(RED), tap(BLUE), { type: 'toggleDq', teamId: RED }])
    const sent = toEntries(state)

    expect(sent.find((e) => e.teamId === RED)?.place).toBe(1)
    expect(sent.find((e) => e.teamId === BLUE)?.place).toBe(2)
  })

  it('removes a team and collapses the empty place', () => {
    const state = run([tap(RED), tap(BLUE), { type: 'remove', teamId: RED }])

    expect(state.groups).toEqual([[BLUE]])
  })

  it('removing one member of a tie leaves the other in place', () => {
    const state = run([
      { type: 'toggleTieArm' },
      tap(RED),
      tap(BLUE),
      { type: 'toggleTieArm' },
      tap(YELLOW),
      { type: 'remove', teamId: RED },
    ])

    expect(state.groups).toEqual([[BLUE], [YELLOW]])
  })

  it('clears a bonus rather than storing a zero', () => {
    const set = run([tap(RED), { type: 'setBonus', teamId: RED, points: 20 }])
    expect(set.bonus).toEqual({ [RED]: 20 })

    expect(reduce(set, { type: 'setBonus', teamId: RED, points: 0 }).bonus).toEqual({})
  })

  it('accepts a typed bonus that is not a round ten', () => {
    const state = run([tap(RED), { type: 'setBonus', teamId: RED, points: 15 }])
    expect(state.bonus[RED]).toBe(15)
  })
})

describe('undo', () => {
  it('steps back through taps one at a time', () => {
    const state = run([tap(RED), tap(BLUE), tap(YELLOW), { type: 'undo' }])

    expect(state.groups).toEqual([[RED], [BLUE]])
  })

  it('composes across ties and disqualifications', () => {
    const state = run([
      { type: 'toggleTieArm' },
      tap(RED),
      tap(BLUE),
      { type: 'toggleTieArm' },
      { type: 'toggleDq', teamId: RED },
      { type: 'undo' },
      { type: 'undo' },
    ])

    expect(state.groups).toEqual([[RED]])
    expect(state.dq).toEqual([])
  })

  it('reverses the last tap even after the multiplier was changed', () => {
    // The multiplier is a setting, not round content. Undo exists to reverse
    // taps, and stepping back through settings makes it useless for that.
    const state = run([
      tap(RED),
      tap(BLUE),
      { type: 'setMultiplier', multiplier: 2 },
      { type: 'undo' },
    ])

    expect(state.groups).toEqual([[RED]])
    expect(state.multiplier).toBe(2)
  })

  it('reverses the last tap even after the game was changed', () => {
    const state = run([tap(RED), tap(BLUE), { type: 'setGame', gameId: 'tug' }, { type: 'undo' }])

    expect(state.groups).toEqual([[RED]])
    expect(state.gameId).toBe('tug')
  })

  it('does nothing on an untouched draft', () => {
    const start = emptyDraft(GAME)
    expect(reduce(start, { type: 'undo' })).toBe(start)
  })

  it('keeps the history bounded', () => {
    // An unbounded stack on a phone left open all evening is a slow leak.
    let state = emptyDraft(GAME)
    for (let i = 0; i < 40; i++) {
      state = reduce(state, { type: 'setBonus', teamId: RED, points: i + 1 })
    }

    expect(state.past.length).toBeLessThanOrEqual(20)
  })

  it('keeps the tie collecting when a tap inside it is stepped back', () => {
    // Undo inside a tie used to drop the group it was collecting into, so the
    // next tap opened a place of its own while the screen still said a tie was
    // being recorded.
    const state = run([
      tap(RED),
      { type: 'toggleTieArm' },
      tap(BLUE),
      tap(YELLOW),
      { type: 'undo' },
      tap(GREEN),
    ])

    expect(state.groups).toEqual([[RED], [BLUE, GREEN]])
    expect(startSlots(state)).toEqual([1, 2])
  })

  it('stops collecting once stepped back past the tap that opened the tie', () => {
    // The group is gone, so the next tap starts a new one rather than joining
    // the place above it.
    const state = run([
      tap(RED),
      { type: 'toggleTieArm' },
      tap(BLUE),
      { type: 'undo' },
      tap(GREEN),
    ])

    expect(state.groups).toEqual([[RED], [GREEN]])
  })

  it('steps back one tap of a finished tie rather than the whole tie', () => {
    // Undo reverses a tap, and each team joining a tie was its own tap. Taking
    // the wrong team back out leaves the rest of the place standing.
    const state = run([
      tap(RED),
      { type: 'toggleTieArm' },
      tap(BLUE),
      tap(YELLOW),
      { type: 'toggleTieArm' },
      { type: 'undo' },
    ])

    expect(state.groups).toEqual([[RED], [BLUE]])
  })

  it('is not polluted by arming a tie', () => {
    const state = run([tap(RED), { type: 'toggleTieArm' }, { type: 'undo' }])

    expect(state.groups).toEqual([])
  })
})

describe('reset', () => {
  it('clears the round but keeps the game and multiplier', () => {
    const state = run([
      { type: 'setMultiplier', multiplier: 2 },
      tap(RED),
      { type: 'toggleDq', teamId: RED },
      { type: 'reset' },
    ])

    expect(state.groups).toEqual([])
    expect(state.dq).toEqual([])
    expect(state.gameId).toBe(GAME)
    expect(state.multiplier).toBe(2)
  })
})

describe('blockingReason', () => {
  it('asks for a game first', () => {
    expect(blockingReason(emptyDraft(null), ALL)).toBe('Pick a game to start')
  })

  it('counts an untouched round as waiting on everybody', () => {
    expect(blockingReason(emptyDraft(GAME), ALL)).toBe('Waiting on 4 teams')
  })

  it('will not send while a tie is still being collected', () => {
    const state = run([tap(RED), tap(BLUE), tap(YELLOW), { type: 'toggleTieArm' }, tap(GREEN)])
    expect(blockingReason(state, ALL)).toBe('Finish the tie first')
  })

  it('names how many teams are outstanding', () => {
    expect(blockingReason(run([tap(RED)]), ALL)).toBe('Waiting on 3 teams')
    expect(blockingReason(run([tap(RED), tap(BLUE), tap(YELLOW)]), ALL)).toBe(
      'Waiting on the last team',
    )
  })

  it('clears once every team is placed', () => {
    const state = run([tap(RED), tap(BLUE), tap(YELLOW), tap(GREEN)])
    expect(blockingReason(state, ALL)).toBeNull()
    expect(placedTeams(state)).toHaveLength(4)
  })
})

describe('reopening a recorded round', () => {
  const team = (teamId: string, place: number | null, extra: Partial<RoundTeam> = {}): RoundTeam => ({
    teamId,
    teamName: teamId,
    place,
    isDisqualified: false,
    points: 0,
    bonusPoints: 0,
    explanation: '',
    ...extra,
  })

  const round = (teams: RoundTeam[], multiplier = 1): RoundSummary => ({
    id: 'r1',
    roundNumber: 3,
    gameId: GAME,
    gameName: 'Baton Relay',
    multiplier,
    isVoided: false,
    voidReason: null,
    teams,
  })

  it('restores the finish order regardless of the order teams arrive in', () => {
    const state = fromRound(round([team(YELLOW, 3), team(RED, 1), team(BLUE, 2)]))

    expect(state.groups).toEqual([[RED], [BLUE], [YELLOW]])
  })

  it('restores a tie as one shared place', () => {
    const state = fromRound(round([team(RED, 1), team(BLUE, 1), team(YELLOW, 3)]))

    expect(state.groups).toEqual([[RED, BLUE], [YELLOW]])
    expect(startSlots(state)).toEqual([1, 3])
  })

  it('restores the game, multiplier, disqualifications and bonuses', () => {
    const state = fromRound(
      round([team(RED, 1, { bonusPoints: 20 }), team(BLUE, 2, { isDisqualified: true })], 2),
    )

    expect(state.gameId).toBe(GAME)
    expect(state.multiplier).toBe(2)
    expect(state.dq).toEqual([BLUE])
    expect(state.bonus).toEqual({ [RED]: 20 })
  })

  it('round trips back to the same entries', () => {
    // What makes a correction safe: reopening a round and confirming it
    // unchanged has to send exactly what is already recorded.
    const original = run([{ type: 'toggleTieArm' }, tap(RED), tap(BLUE), { type: 'toggleTieArm' }, tap(YELLOW)])
    const recorded = round(
      toEntries(original).map((e) => team(e.teamId, e.place, { isDisqualified: e.isDisqualified })),
    )

    expect(toEntries(fromRound(recorded))).toEqual(toEntries(original))
  })

  it('starts with a clean undo stack', () => {
    // Undo steps back through this session's taps, not into a state that was
    // never on screen.
    const state = fromRound(round([team(RED, 1)]))

    expect(state.past).toEqual([])
  })
})
