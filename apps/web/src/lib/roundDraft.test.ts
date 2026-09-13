import { describe, expect, it } from 'vitest'
import {
  blockingReason,
  emptyDraft,
  placedTeams,
  roundDraftReducer as reduce,
  toEntries,
  type DraftAction,
  type DraftState,
} from './roundDraft'

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

  it('ignores a team that is marked absent', () => {
    const state = run([{ type: 'toggleAbsent', teamId: GREEN }, tap(GREEN)])

    expect(placedTeams(state)).toEqual([])
    expect(state.absent).toEqual([GREEN])
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

  it('tieUp merges a row into the one above it, for a tie noticed afterwards', () => {
    const state = run([tap(RED), tap(BLUE), { type: 'tieUp', groupIndex: 1 }])

    expect(state.groups).toEqual([[RED, BLUE]])
  })

  it('reaches the same state whichever way the tie was recorded', () => {
    const armed = run([{ type: 'toggleTieArm' }, tap(RED), tap(BLUE)])
    const afterwards = run([tap(RED), tap(BLUE), { type: 'tieUp', groupIndex: 1 }])

    expect(armed.groups).toEqual(afterwards.groups)
  })

  it('refuses to tie the first row upwards', () => {
    const before = run([tap(RED), tap(BLUE)])
    const after = reduce(before, { type: 'tieUp', groupIndex: 0 })

    expect(after).toBe(before)
  })
})

describe('reordering', () => {
  it('swaps a row with the one above', () => {
    const state = run([tap(RED), tap(BLUE), { type: 'move', groupIndex: 1, direction: 'up' }])

    expect(state.groups).toEqual([[BLUE], [RED]])
  })

  it('swaps a row with the one below', () => {
    const state = run([tap(RED), tap(BLUE), { type: 'move', groupIndex: 0, direction: 'down' }])

    expect(state.groups).toEqual([[BLUE], [RED]])
  })

  it('does nothing at either end', () => {
    const before = run([tap(RED), tap(BLUE)])

    expect(reduce(before, { type: 'move', groupIndex: 0, direction: 'up' })).toBe(before)
    expect(reduce(before, { type: 'move', groupIndex: 1, direction: 'down' })).toBe(before)
  })
})

describe('corrections', () => {
  it('toggles disqualification without changing the order', () => {
    const state = run([tap(RED), tap(BLUE), { type: 'toggleDq', teamId: RED }])

    expect(state.dq).toEqual([RED])
    expect(state.groups).toEqual([[RED], [BLUE]])

    const undone = reduce(state, { type: 'toggleDq', teamId: RED })
    expect(undone.dq).toEqual([])
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

  it('marking a team absent takes it out of the finish order', () => {
    // The two must never disagree about whether a team took part.
    const state = run([tap(RED), tap(BLUE), { type: 'toggleAbsent', teamId: RED }])

    expect(state.groups).toEqual([[BLUE]])
    expect(state.absent).toEqual([RED])
  })

  it('clears a bonus rather than storing a zero', () => {
    const set = run([tap(RED), { type: 'setBonus', teamId: RED, points: 20 }])
    expect(set.bonus).toEqual({ [RED]: 20 })

    const cleared = reduce(set, { type: 'setBonus', teamId: RED, points: 0 })
    expect(cleared.bonus).toEqual({})
  })
})

describe('undo', () => {
  it('steps back through taps one at a time', () => {
    const state = run([tap(RED), tap(BLUE), tap(YELLOW), { type: 'undo' }])

    expect(state.groups).toEqual([[RED], [BLUE]])
  })

  it('composes across ties and disqualifications', () => {
    const state = run([
      tap(RED),
      tap(BLUE),
      { type: 'tieUp', groupIndex: 1 },
      { type: 'toggleDq', teamId: RED },
      { type: 'undo' },
      { type: 'undo' },
    ])

    expect(state.groups).toEqual([[RED], [BLUE]])
    expect(state.dq).toEqual([])
  })

  it('does nothing on an untouched draft', () => {
    const start = emptyDraft(GAME)
    expect(reduce(start, { type: 'undo' })).toBe(start)
  })

  it('keeps the history bounded', () => {
    // Twenty steps is plenty for a round, and an unbounded stack on a phone
    // left open all evening is a slow leak.
    let state = emptyDraft(GAME)
    for (let i = 0; i < 40; i++) {
      state = reduce(state, { type: 'setMultiplier', multiplier: i % 2 === 0 ? 1 : 2 })
    }

    expect(state.past.length).toBeLessThanOrEqual(20)
  })

  it('is not polluted by arming a tie', () => {
    // Arming changes nothing about the round, so stepping back through mode
    // changes would make undo useless for actual mistakes.
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
    expect(blockingReason(emptyDraft(null), ALL)).toBe('Pick a game')
  })

  it('asks for a first tap', () => {
    expect(blockingReason(emptyDraft(GAME), ALL)).toBe('Tap a team to start')
  })

  it('waits until every team is accounted for', () => {
    const state = run([tap(RED), tap(BLUE), tap(YELLOW)])
    expect(blockingReason(state, ALL)).not.toBeNull()
  })

  it('clears once every team is placed', () => {
    const state = run([tap(RED), tap(BLUE), tap(YELLOW), tap(GREEN)])
    expect(blockingReason(state, ALL)).toBeNull()
  })

  it('counts an absent team as accounted for', () => {
    const state = run([tap(RED), tap(BLUE), tap(YELLOW), { type: 'toggleAbsent', teamId: GREEN }])
    expect(blockingReason(state, ALL)).toBeNull()
  })
})

describe('toEntries', () => {
  it('gives tied teams the same place', () => {
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
    expect(entries.find((e) => e.teamId === YELLOW)?.place).toBe(2)
  })

  it('sends an absent team with a null place rather than omitting it', () => {
    const state = run([tap(RED), { type: 'toggleAbsent', teamId: GREEN }])
    const green = toEntries(state).find((e) => e.teamId === GREEN)

    expect(green).toBeDefined()
    expect(green?.place).toBeNull()
  })

  it('carries disqualification and bonus through', () => {
    const state = run([
      tap(RED),
      tap(BLUE),
      { type: 'toggleDq', teamId: RED },
      { type: 'setBonus', teamId: BLUE, points: 20 },
    ])

    const entries = toEntries(state)

    expect(entries.find((e) => e.teamId === RED)?.isDisqualified).toBe(true)
    expect(entries.find((e) => e.teamId === BLUE)?.bonus).toBe(20)
  })
})
