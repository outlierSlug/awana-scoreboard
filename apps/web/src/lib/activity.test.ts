import { describe, expect, it } from 'vitest'
import { describeActivity, groupActivity } from './activity'
import type { ActivityEntry, ActivitySession } from './types'

const GAME = '01a09870-0000-7000-8000-000000000001'
const OTHER_GAME = '01a09870-0000-7000-8000-000000000004'
const BLUE = '01a09870-0000-7000-8000-000000000002'
const RED = '01a09870-0000-7000-8000-000000000005'
const PROFILE = '01a09870-0000-7000-8000-000000000003'

const names = {
  [GAME]: 'Baton Relay',
  [OTHER_GAME]: 'Tug-of-War',
  [BLUE]: 'Blue',
  [RED]: 'Red',
  [PROFILE]: 'Official AWANA',
}

const TNT: ActivitySession = { id: 's1', divisionName: 'T&T', date: '2026-10-02' }
const SPARKS: ActivitySession = { id: 's2', divisionName: 'Sparks', date: '2026-10-02' }

function entry(
  action: string,
  data: Record<string, unknown> | null,
  extra: Partial<ActivityEntry> = {},
): ActivityEntry {
  return {
    id: Math.random().toString(36),
    at: '2026-10-02T02:15:00Z',
    action,
    entityType: 'Session',
    entityId: 'y',
    actorId: null,
    actorName: null,
    session: null,
    data,
    ...extra,
  }
}

describe('describeActivity', () => {
  it('says how a round finished, not only that it was recorded', () => {
    const line = describeActivity(
      entry('round.recorded', {
        RoundNumber: 3,
        GameId: GAME,
        Multiplier: 2,
        Results: [
          { TeamId: RED, Place: 2, IsDisqualified: false, Points: 60 },
          { TeamId: BLUE, Place: 1, IsDisqualified: false, Points: 80 },
        ],
      }),
      names,
    )

    expect(line.text).toBe('Recorded round 3 · Baton Relay · ×2')
    expect(line.details).toEqual(['1st Blue +80 · 2nd Red +60'])
    expect(line.text).not.toContain(GAME)
  })

  it('shows what an edit changed', () => {
    const line = describeActivity(
      entry('round.edited', {
        RoundNumber: 1,
        Before: {
          GameId: GAME,
          Multiplier: 1,
          Results: [
            { TeamId: BLUE, Place: 1, Points: 40 },
            { TeamId: RED, Place: 2, Points: 30 },
          ],
        },
        After: {
          GameId: OTHER_GAME,
          Multiplier: 1,
          Results: [
            { TeamId: RED, Place: 1, Points: 40 },
            { TeamId: BLUE, Place: 2, Points: 30 },
          ],
        },
      }),
      names,
    )

    expect(line.text).toBe('Edited round 1 · Tug-of-War')
    expect(line.details).toEqual([
      'Game: Baton Relay → Tug-of-War',
      'Was: 1st Blue +40 · 2nd Red +30',
      'Now: 1st Red +40 · 2nd Blue +30',
    ])
  })

  it('still describes an edit recorded before edits kept their detail', () => {
    expect(describeActivity(entry('round.edited', { RoundNumber: 4 }), names)).toMatchObject({
      text: 'Edited round 4',
      details: [],
    })
  })

  it('marks a disqualification in the finish order', () => {
    const line = describeActivity(
      entry('round.recorded', {
        RoundNumber: 2,
        GameId: GAME,
        Results: [{ TeamId: BLUE, Place: 1, IsDisqualified: true, Points: 0 }],
      }),
      names,
    )
    expect(line.details).toEqual(['Blue DQ'])
  })

  it('keeps the reason a round was cleared', () => {
    const line = describeActivity(entry('round.voided', { RoundNumber: 4, GameId: GAME, Reason: 'Wrong game' }), names)
    expect(line).toMatchObject({ text: 'Cleared round 4 · Baton Relay', details: ['“Wrong game”'] })
  })

  it('names the team and the numbers on a headcount change', () => {
    const one = describeActivity(entry('attendance.updated', { Teams: [{ TeamId: BLUE, From: 12, To: 14 }] }), names)
    expect(one.text).toBe("Set Blue's headcount to 14 (was 12)")

    const cleared = describeActivity(entry('attendance.updated', { Teams: [{ TeamId: RED, From: 9, To: null }] }), names)
    expect(cleared.text).toBe("Cleared Red's headcount (was 9)")

    const old = describeActivity(entry('attendance.updated', { Recorded: 2 }), names)
    expect(old.text).toBe('Updated headcounts for 2 teams')
  })

  it('reads a bonus and a penalty differently, and keeps why a bonus was removed', () => {
    expect(describeActivity(entry('adjustment.added', { TeamId: BLUE, Points: 5, Reason: 'Verse' }), names).text).toBe(
      'Gave Blue 5 points',
    )
    expect(describeActivity(entry('adjustment.added', { TeamId: BLUE, Points: -1, Reason: 'Late' }), names).text).toBe(
      'Took Blue 1 point',
    )

    const removed = describeActivity(entry('adjustment.voided', { TeamId: BLUE, Points: 5, Reason: 'Verse' }), names)
    expect(removed).toMatchObject({ text: "Removed Blue's +5 adjustment", details: ['“Verse”'] })
  })

  it('says which rules a session started under', () => {
    const line = describeActivity(
      entry('session.started', { Scoring: 'Official AWANA', PlacePoints: [40, 30, 20, 10] }, { session: TNT }),
      names,
    )
    expect(line).toMatchObject({ text: 'Started the session', details: ['Scored with Official AWANA · 40 / 30 / 20 / 10'] })
  })

  it('says what the scoring was before it was changed', () => {
    const changed = describeActivity(entry('session.scoring_set', { From: null, ScoringProfileId: PROFILE }), names)
    expect(changed).toMatchObject({ text: 'Set the scoring to Official AWANA', details: ['Was the default rules'] })

    // Older entries did not record the previous value at all.
    const old = describeActivity(entry('session.scoring_set', { ScoringProfileId: null }), names)
    expect(old).toMatchObject({ text: 'Set the scoring to the default rules', details: [] })
  })

  it('names a deleted session that could not be placed in a group', () => {
    const line = describeActivity(
      entry('session.deleted', { DivisionName: 'T&T', Date: '2026-09-14', ClearedRounds: 2, ClearedAdjustments: 0 }),
      names,
    )
    expect(line.text).toMatch(/^Deleted T&T session for .*September 14, 2026$/)
    expect(line.details).toEqual(['It had 2 cleared rounds, which went with it'])
  })

  it('tells a rename apart from an edit to the rules text', () => {
    const renamed = entry('game.updated', { Before: { Name: 'Steal' }, After: { Name: 'Steal the Bacon' } })
    const notes = entry('game.updated', { Before: { Name: 'Steal', Notes: 'a' }, After: { Name: 'Steal', Notes: 'b' } })

    expect(describeActivity(renamed, names).text).toBe('Renamed Steal to Steal the Bacon')
    expect(describeActivity(notes, names).text).toBe('Edited the rules for Steal')
  })

  it('shows how a scoring table changed', () => {
    const line = describeActivity(
      entry('scoring_profile.updated', {
        Before: { Name: 'House', Config: { PlacePoints: [40, 30, 20, 10], TieRule: 0 } },
        After: { Name: 'House', Config: { PlacePoints: [50, 30, 20, 10], TieRule: 1 } },
      }),
      names,
    )
    expect(line.details).toEqual([
      'Points: 40 / 30 / 20 / 10 → 50 / 30 / 20 / 10',
      'Changed how ties, disqualifications or rounding are handled',
    ])
  })

  it('uses the words a volunteer would for roles', () => {
    const line = describeActivity(
      entry('person.role_changed', { Email: 'a@example.com', From: 'Scorekeeper', To: 'GamesLeader' }),
      names,
    )
    expect(line.text).toBe('Changed a@example.com from Scorekeeper to Games leader')
    expect(describeActivity(entry('person.added', { Email: 'b@example.com', Role: 'Admin' }), names).text).toBe(
      'Added b@example.com as an Admin',
    )
  })

  it('still says something readable about an action it has never heard of', () => {
    expect(describeActivity(entry('scoring_profile.something_new', null), names)).toMatchObject({
      category: 'other',
      text: 'Scoring profile something new',
    })
  })

  it('does not fall over when the data it expects is missing', () => {
    expect(() => describeActivity(entry('adjustment.added', null), names)).not.toThrow()
    expect(() => describeActivity(entry('round.edited', { Before: 'nonsense' }), names)).not.toThrow()
    expect(describeActivity(entry('round.recorded', {}), names).text).toBe('Recorded round')
  })
})

describe('groupActivity', () => {
  it('keeps a night together even when two divisions interleave', () => {
    const groups = groupActivity([
      entry('round.recorded', {}, { session: TNT, at: '2026-10-03T03:05:00Z' }),
      entry('round.recorded', {}, { session: SPARKS, at: '2026-10-03T03:04:00Z' }),
      entry('round.recorded', {}, { session: TNT, at: '2026-10-03T03:03:00Z' }),
      entry('round.recorded', {}, { session: SPARKS, at: '2026-10-03T03:02:00Z' }),
    ])

    expect(groups.map((g) => g.session?.divisionName)).toEqual(['T&T', 'Sparks'])
    expect(groups.map((g) => g.entries.length)).toEqual([2, 2])
  })

  it('puts changes outside any session together by day', () => {
    const groups = groupActivity([
      entry('game.created', { Name: 'A' }, { at: '2026-09-30T18:00:00Z' }),
      entry('person.added', { Email: 'x@y.z' }, { at: '2026-09-30T17:00:00Z' }),
    ])

    expect(groups).toHaveLength(1)
    expect(groups[0].session).toBeNull()
    expect(groups[0].entries).toHaveLength(2)
  })

  it('notices when a night was set up on one day and played on another', () => {
    const [group] = groupActivity([
      entry('session.started', {}, { session: TNT, at: '2026-10-03T02:00:00Z' }),
      entry('session.scoring_set', {}, { session: TNT, at: '2026-09-28T18:00:00Z' }),
    ])

    expect(group.spansDays).toBe(true)
  })
})
