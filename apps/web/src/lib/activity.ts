import { formatDate, formatPoints } from '@/lib/format'
import { roleLabel } from '@/lib/roles'
import type { ActivityEntry, ActivitySession } from '@/lib/types'

export type ActivityCategory = 'rounds' | 'sessions' | 'points' | 'games' | 'scoring' | 'people' | 'other'

export interface ActivityLine {
  category: ActivityCategory
  /** What happened, as a sentence without its subject: the actor is shown beside it. */
  text: string
  /** The specifics: a finish order, a reason, what something was before. */
  details: string[]
}

type Data = Record<string, unknown>

interface AuditResult {
  TeamId?: string
  Place?: number | null
  IsDisqualified?: boolean
  Points?: number
}

/**
 * Turns an audit entry into something a person reads.
 *
 * The log stores ids rather than names, so renaming a game does not rewrite
 * history, and `names` is the page's lookup for them. Entries written before
 * an action started recording more detail still get their sentence, just with
 * fewer specifics under it; nothing here assumes a field is present. An action
 * this does not recognize gets a readable line made from its own name, so a
 * new kind of entry shows up plainly instead of vanishing.
 */
export function describeActivity(entry: ActivityEntry, names: Record<string, string>): ActivityLine {
  const data: Data = entry.data ?? {}

  const str = (key: string, from: Data = data) => (typeof from[key] === 'string' ? (from[key] as string) : undefined)
  const num = (key: string, from: Data = data) => (typeof from[key] === 'number' ? (from[key] as number) : undefined)
  const obj = (key: string, from: Data = data) =>
    from[key] && typeof from[key] === 'object' && !Array.isArray(from[key]) ? (from[key] as Data) : undefined
  const has = (key: string, from: Data = data) => key in from
  const nameOf = (id: string | undefined | null) => (id ? names[id] ?? names[id.toLowerCase()] : undefined)

  const line = (category: ActivityCategory, text: string, ...details: (string | undefined | false)[]): ActivityLine => ({
    category,
    text,
    details: details.filter((d): d is string => typeof d === 'string' && d.length > 0),
  })

  switch (entry.action) {
    // ------------------------------------------------------------ rounds

    case 'round.recorded':
      return line(
        'rounds',
        roundTitle('Recorded', num('RoundNumber'), nameOf(str('GameId')), num('Multiplier')),
        results(data.Results, names),
      )

    case 'round.edited': {
      const before = obj('Before')
      const after = obj('After')

      // Older edits recorded only the round's number.
      if (!before || !after) return line('rounds', `Edited round ${num('RoundNumber') ?? ''}`.trim())

      const gameBefore = nameOf(str('GameId', before))
      const gameAfter = nameOf(str('GameId', after))
      const multBefore = num('Multiplier', before)
      const multAfter = num('Multiplier', after)
      const wasResults = results(before.Results, names)
      const nowResults = results(after.Results, names)

      const changes = [
        gameBefore !== gameAfter && `Game: ${gameBefore ?? '?'} → ${gameAfter ?? '?'}`,
        multBefore !== multAfter && `Multiplier: ×${multBefore ?? 1} → ×${multAfter ?? 1}`,
        wasResults !== nowResults && wasResults && `Was: ${wasResults}`,
        wasResults !== nowResults && nowResults && `Now: ${nowResults}`,
      ]

      return line(
        'rounds',
        roundTitle('Edited', num('RoundNumber'), gameAfter, undefined),
        ...(changes.some(Boolean) ? changes : ['Saved without changing anything']),
      )
    }

    case 'round.voided':
      return line('rounds', roundTitle('Cleared', num('RoundNumber'), nameOf(str('GameId')), undefined), quote(str('Reason')))

    // ---------------------------------------------------------- sessions

    case 'session.started':
      return line('sessions', sessionTitle('Started', entry.session), scoredWith(str('Scoring'), data.PlacePoints))
    case 'session.finished':
      return line('sessions', sessionTitle('Finished', entry.session))
    case 'session.reopened':
      return line('sessions', sessionTitle('Reopened', entry.session))

    case 'session.scoring_set': {
      // Null means the church's default, chosen on purpose, not a gap.
      const to = nameOf(str('ScoringProfileId')) ?? 'the default rules'
      const from = has('From') ? (nameOf(str('From')) ?? 'the default rules') : undefined
      return line('sessions', `Set the scoring to ${to}`, from && `Was ${from}`)
    }

    case 'session.deleted': {
      const rounds = num('ClearedRounds') ?? 0
      const adjustments = num('ClearedAdjustments') ?? 0
      const leftovers = [plural(rounds, 'cleared round'), plural(adjustments, 'cleared adjustment')].filter(Boolean)
      const date = str('Date')

      return line(
        'sessions',
        entry.session
          ? 'Deleted the session'
          : `Deleted ${str('DivisionName') ?? 'a'} session${date ? ` for ${formatDate(date)}` : ''}`,
        leftovers.length > 0 && `It had ${leftovers.join(' and ')}, which went with it`,
      )
    }

    // ------------------------------------------------------------ points

    case 'adjustment.added': {
      const points = num('Points') ?? 0
      return line(
        'points',
        `${points >= 0 ? 'Gave' : 'Took'} ${nameOf(str('TeamId')) ?? 'a team'} ${formatPoints(Math.abs(points))} ${
          Math.abs(points) === 1 ? 'point' : 'points'
        }`,
        quote(str('Reason')),
      )
    }

    case 'adjustment.voided': {
      const points = num('Points') ?? 0
      return line(
        'points',
        `Removed ${nameOf(str('TeamId')) ?? 'a team'}'s ${signed(points)} adjustment`,
        quote(str('Reason')),
      )
    }

    case 'attendance.updated': {
      const teams = Array.isArray(data.Teams) ? (data.Teams as Data[]) : undefined

      // Older entries only counted how many teams had a number.
      if (!teams) {
        const recorded = num('Recorded') ?? 0
        return line('points', `Updated headcounts for ${recorded} ${recorded === 1 ? 'team' : 'teams'}`)
      }

      const sentences = teams.map((t) => {
        const team = nameOf(str('TeamId', t)) ?? 'a team'
        const from = num('From', t)
        const to = num('To', t)
        const was = from === undefined ? '' : ` (was ${from})`
        return to === undefined ? `Cleared ${team}'s headcount${was}` : `Set ${team}'s headcount to ${to}${was}`
      })

      return sentences.length === 1
        ? line('points', sentences[0])
        : line('points', 'Updated headcounts', ...sentences)
    }

    // ------------------------------------------------------------- games

    case 'game.created':
      return line('games', `Added the game ${str('Name') ?? ''}`.trim())

    case 'game.updated': {
      const before = obj('Before') ?? {}
      const after = obj('After') ?? {}
      const nameBefore = str('Name', before)
      const nameAfter = str('Name', after)

      if (nameBefore && nameAfter && nameBefore !== nameAfter) {
        return line('games', `Renamed ${nameBefore} to ${nameAfter}`)
      }

      return line(
        'games',
        str('Notes', before) !== str('Notes', after)
          ? `Edited the rules for ${nameAfter ?? 'a game'}`
          : `Edited ${nameAfter ?? 'a game'}`,
      )
    }

    case 'game.retired':
      return line('games', `Retired ${str('Name') ?? 'a game'}`)
    case 'game.restored':
      return line('games', `Brought back ${str('Name') ?? 'a game'}`)
    case 'game.deleted':
      return line('games', `Deleted the game ${str('Name') ?? ''}`.trim())
    case 'game.reordered':
      return line('games', 'Reordered the games')

    // ----------------------------------------------------------- scoring

    case 'scoring_profile.created':
      return line('scoring', `Created the scoring rules ${str('Name') ?? ''}`.trim(), table(data.PlacePoints))

    case 'scoring_profile.updated': {
      const before = obj('Before') ?? {}
      const after = obj('After') ?? {}
      const nameBefore = str('Name', before)
      const nameAfter = str('Name', after)
      const configBefore = obj('Config', before) ?? {}
      const configAfter = obj('Config', after) ?? {}

      const tableBefore = table(configBefore.PlacePoints)
      const tableAfter = table(configAfter.PlacePoints)

      // Everything that is not the points table, compared as a whole: a
      // leader wants to know that tie handling changed, and the log keeps the
      // exact values for anyone who needs them.
      const { PlacePoints: _a, ...restBefore } = configBefore
      const { PlacePoints: _b, ...restAfter } = configAfter

      return line(
        'scoring',
        `Edited ${nameAfter ?? 'scoring rules'}`,
        nameBefore && nameAfter && nameBefore !== nameAfter && `Renamed from ${nameBefore}`,
        tableBefore !== tableAfter && tableBefore && tableAfter && `Points: ${tableBefore} → ${tableAfter}`,
        JSON.stringify(restBefore) !== JSON.stringify(restAfter) &&
          'Changed how ties, disqualifications or rounding are handled',
      )
    }

    case 'scoring_profile.default_set':
      return line('scoring', `Made ${str('Name') ?? 'a set of rules'} the default`)
    case 'scoring_profile.retired':
      return line('scoring', `Retired ${str('Name') ?? 'a set of rules'}`)
    case 'scoring_profile.restored':
      return line('scoring', `Brought back ${str('Name') ?? 'a set of rules'}`)
    case 'scoring_profile.deleted':
      return line('scoring', `Deleted the scoring rules ${str('Name') ?? ''}`.trim())
    case 'scoring_profile.duplicated':
      return line('scoring', `Copied ${str('From') ?? 'a set of rules'} as ${str('Name') ?? 'a new set'}`)

    // ------------------------------------------------------------ people

    case 'person.added':
      return line('people', `Added ${str('Email') ?? 'someone'} as ${article(roleLabel(str('Role')))}`)
    case 'person.role_changed':
      return line(
        'people',
        `Changed ${str('Email') ?? 'someone'} from ${roleLabel(str('From')) ?? '?'} to ${roleLabel(str('To')) ?? '?'}`,
      )
    case 'person.deactivated':
      return line('people', `Deactivated ${str('Email') ?? 'someone'}`)
    case 'person.reactivated':
      return line('people', `Reactivated ${str('Email') ?? 'someone'}`)

    default:
      return line('other', humanize(entry.action))
  }
}

// ------------------------------------------------------------------ grouping

export interface ActivityGroup {
  key: string
  /** The night, for entries that happened in one. Null for catalog, scoring and people changes. */
  session: ActivitySession | null
  /** For groups without a session: the local day they happened on. */
  day: string | null
  entries: ActivityEntry[]
  /** True when a group's entries fall on more than one local day, so times need a day beside them. */
  spansDays: boolean
}

/**
 * Puts each night's entries together, and everything else together by day.
 *
 * The unit people ask about is a night: "what happened on the T&T night". Two
 * divisions often run at once, so their entries interleave in time, and grouping
 * only runs of consecutive entries would chop each night into fragments. Groups
 * are ordered by their most recent entry, so whatever changed last is at the top.
 */
export function groupActivity(entries: ActivityEntry[]): ActivityGroup[] {
  const groups = new Map<string, ActivityGroup>()

  for (const entry of entries) {
    const day = localDay(entry.at)
    const key = entry.session ? `session:${entry.session.id}` : `day:${day}`

    let group = groups.get(key)
    if (!group) {
      group = { key, session: entry.session, day: entry.session ? null : day, entries: [], spansDays: false }
      groups.set(key, group)
    }

    if (group.entries.length > 0 && localDay(group.entries[0].at) !== day) group.spansDays = true
    group.entries.push(entry)
  }

  // Entries arrive newest first, so a group's first entry is its most recent,
  // and Map keeps first-seen order: already newest group first.
  return [...groups.values()]
}

function localDay(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  })
}

// ------------------------------------------------------------------ phrasing

function roundTitle(verb: string, number: number | undefined, game: string | undefined, multiplier: number | undefined) {
  return [
    `${verb} round${number === undefined ? '' : ` ${number}`}`,
    game,
    multiplier && multiplier !== 1 ? `×${multiplier}` : undefined,
  ]
    .filter(Boolean)
    .join(' · ')
}

/** Inside a session's group the session is already named, so the sentence stays short. */
function sessionTitle(verb: string, session: ActivitySession | null): string {
  return session ? `${verb} the session` : `${verb} a session`
}

function scoredWith(name: string | undefined, placePoints: unknown): string | undefined {
  const points = table(placePoints)
  if (!name && !points) return undefined
  return `Scored with ${[name, points].filter(Boolean).join(' · ')}`
}

/** "1st Blue +40 · 1st Red +40 · 3rd Green DQ" */
function results(value: unknown, names: Record<string, string>): string {
  if (!Array.isArray(value) || value.length === 0) return ''

  return (value as AuditResult[])
    .slice()
    .sort((a, b) => (a.Place ?? Number.MAX_SAFE_INTEGER) - (b.Place ?? Number.MAX_SAFE_INTEGER))
    .map((r) => {
      const team = (r.TeamId && (names[r.TeamId] ?? names[r.TeamId.toLowerCase()])) || 'a team'
      if (r.IsDisqualified) return `${team} DQ`
      if (r.Place === null || r.Place === undefined) return `${team} did not play`
      return `${ordinal(r.Place)} ${team} ${signed(r.Points ?? 0)}`
    })
    .join(' · ')
}

function table(value: unknown): string {
  return Array.isArray(value) && value.every((v) => typeof v === 'number')
    ? (value as number[]).map(formatPoints).join(' / ')
    : ''
}

function ordinal(n: number): string {
  const teen = n % 100 >= 11 && n % 100 <= 13
  const suffix = teen ? 'th' : ({ 1: 'st', 2: 'nd', 3: 'rd' } as Record<number, string>)[n % 10] ?? 'th'
  return `${n}${suffix}`
}

function signed(points: number): string {
  return `${points < 0 ? '-' : '+'}${formatPoints(Math.abs(points))}`
}

function plural(count: number, noun: string): string {
  return count > 0 ? `${count} ${noun}${count === 1 ? '' : 's'}` : ''
}

function quote(text: string | undefined): string | undefined {
  return text ? `“${text}”` : undefined
}

/** "a Scorekeeper", "an Admin". */
function article(label: string | null): string {
  if (!label) return 'someone'
  return /^[AEIOU]/i.test(label) ? `an ${label}` : `a ${label}`
}

/** "scoring_profile.default_set" becomes "Scoring profile default set". */
function humanize(action: string): string {
  const words = action.replace(/[._]/g, ' ').trim()
  return words.charAt(0).toUpperCase() + words.slice(1)
}
