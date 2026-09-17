import { describe, expect, it } from 'vitest'
import { describeActivity } from './activity'
import type { ActivityEntry } from './types'

const GAME = '01a09870-0000-7000-8000-000000000001'
const TEAM = '01a09870-0000-7000-8000-000000000002'
const PROFILE = '01a09870-0000-7000-8000-000000000003'

const names = { [GAME]: 'Baton Relay', [TEAM]: 'Yellow', [PROFILE]: 'Official AWANA' }

function entry(action: string, data: Record<string, unknown> | null): ActivityEntry {
  return {
    id: 'x',
    at: '2026-10-02T02:15:00Z',
    action,
    entityType: 'Session',
    entityId: 'y',
    actorId: null,
    actorName: null,
    session: null,
    data,
  }
}

describe('describeActivity', () => {
  it('names the game a round was played on, rather than its id', () => {
    const line = describeActivity(entry('round.recorded', { RoundNumber: 3, GameId: GAME, Multiplier: 1 }), names)
    expect(line.text).toBe('Recorded round 3 · Baton Relay')
    expect(line.text).not.toContain(GAME)
  })

  it('mentions a multiplier only when there is one', () => {
    const line = describeActivity(entry('round.recorded', { RoundNumber: 9, GameId: GAME, Multiplier: 2 }), names)
    expect(line.text).toBe('Recorded round 9 · Baton Relay · ×2')
  })

  it('keeps the reason a round was cleared as its own line', () => {
    const line = describeActivity(entry('round.voided', { RoundNumber: 4, Reason: 'Wrong game' }), names)
    expect(line).toMatchObject({ text: 'Cleared round 4', detail: 'Wrong game' })
  })

  it('reads a bonus and a penalty differently', () => {
    expect(describeActivity(entry('adjustment.added', { TeamId: TEAM, Points: 5, Reason: 'Verse' }), names).text)
      .toBe('Gave Yellow 5 points')
    expect(describeActivity(entry('adjustment.added', { TeamId: TEAM, Points: -1, Reason: 'Late' }), names).text)
      .toBe('Took Yellow 1 point')
  })

  it('says the default when a session was put back on it', () => {
    expect(describeActivity(entry('session.scoring_set', { ScoringProfileId: null }), names).text)
      .toBe('Set scoring to the default')
    expect(describeActivity(entry('session.scoring_set', { ScoringProfileId: PROFILE }), names).text)
      .toBe('Set scoring to Official AWANA')
  })

  it('distinguishes a rename from an edit to the notes', () => {
    const renamed = entry('game.updated', { Before: { Name: 'Steal' }, After: { Name: 'Steal the Bacon' } })
    const notes = entry('game.updated', { Before: { Name: 'Steal' }, After: { Name: 'Steal' } })

    expect(describeActivity(renamed, names).text).toBe('Renamed Steal to Steal the Bacon')
    expect(describeActivity(notes, names).text).toBe('Edited Steal')
  })

  it('uses the words a volunteer would for roles', () => {
    const line = describeActivity(
      entry('person.role_changed', { Email: 'a@example.com', From: 'Scorekeeper', To: 'GamesLeader' }),
      names,
    )
    expect(line.text).toBe('Changed a@example.com from Scorekeeper to Games leader')

    expect(describeActivity(entry('person.added', { Email: 'b@example.com', Role: 'Admin' }), names).text)
      .toBe('Added b@example.com as an Admin')
  })

  it('still says something readable about an action it has never heard of', () => {
    const line = describeActivity(entry('scoring_profile.something_new', null), names)
    expect(line).toMatchObject({ category: 'other', text: 'Scoring profile something new' })
  })

  it('does not fall over when the data it expects is missing', () => {
    expect(() => describeActivity(entry('adjustment.added', null), names)).not.toThrow()
    expect(describeActivity(entry('round.recorded', {}), names).text).toBe('Recorded round')
  })
})
