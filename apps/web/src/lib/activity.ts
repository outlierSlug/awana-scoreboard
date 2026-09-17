import { formatDate, formatPoints } from '@/lib/format'
import { roleLabel } from '@/lib/roles'
import type { ActivityEntry } from '@/lib/types'

export type ActivityCategory = 'rounds' | 'sessions' | 'points' | 'games' | 'scoring' | 'people' | 'other'

export interface ActivityLine {
  category: ActivityCategory
  /** What happened, as a sentence without its subject: the actor is shown beside it. */
  text: string
  /** A reason, a before and after, or anything else worth a second line. */
  detail?: string
}

/**
 * Turns an audit entry into something a person reads.
 *
 * The log stores what each action recorded, with ids rather than names so that
 * renaming a game does not rewrite history. `names` is the page's lookup for
 * those ids. An action this does not recognize still gets a readable line made
 * from its own name, so a new kind of entry shows up plainly instead of
 * vanishing from the list.
 */
export function describeActivity(entry: ActivityEntry, names: Record<string, string>): ActivityLine {
  const data = entry.data ?? {}
  const text = (key: string) => (typeof data[key] === 'string' ? (data[key] as string) : undefined)
  const num = (key: string) => (typeof data[key] === 'number' ? (data[key] as number) : undefined)
  const name = (key: string) => {
    const id = text(key)
    return id ? names[id] ?? names[id.toLowerCase()] : undefined
  }
  const nested = (key: string, field: string) => {
    const value = data[key]
    return value && typeof value === 'object' ? (value as Record<string, unknown>)[field] : undefined
  }

  switch (entry.action) {
    case 'round.recorded': {
      const multiplier = num('Multiplier')
      const game = name('GameId')
      return {
        category: 'rounds',
        text: `Recorded round ${num('RoundNumber') ?? ''}${game ? ` · ${game}` : ''}${
          multiplier && multiplier !== 1 ? ` · ×${multiplier}` : ''
        }`.trim(),
      }
    }
    case 'round.edited':
      return { category: 'rounds', text: `Edited round ${num('RoundNumber') ?? ''}`.trim() }
    case 'round.voided':
      return {
        category: 'rounds',
        text: `Cleared round ${num('RoundNumber') ?? ''}`.trim(),
        detail: text('Reason'),
      }

    case 'session.started':
      return { category: 'sessions', text: 'Started the session' }
    case 'session.finished':
      return { category: 'sessions', text: 'Finished the session' }
    case 'session.reopened':
      return { category: 'sessions', text: 'Reopened the session' }
    case 'session.scoring_set':
      return {
        category: 'sessions',
        // Null is a deliberate choice of "whatever the default is", not a gap.
        text: `Set scoring to ${name('ScoringProfileId') ?? 'the default'}`,
      }
    case 'session.deleted': {
      const date = text('Date')
      return { category: 'sessions', text: `Deleted the session${date ? ` for ${formatDate(date)}` : ''}` }
    }

    case 'adjustment.added': {
      const points = num('Points') ?? 0
      return {
        category: 'points',
        text: `${points >= 0 ? 'Gave' : 'Took'} ${name('TeamId') ?? 'a team'} ${formatPoints(Math.abs(points))} ${
          Math.abs(points) === 1 ? 'point' : 'points'
        }`,
        detail: text('Reason'),
      }
    }
    case 'adjustment.voided': {
      const points = num('Points') ?? 0
      return {
        category: 'points',
        text: `Removed ${name('TeamId') ?? 'a team'}'s ${points >= 0 ? '+' : '-'}${formatPoints(Math.abs(points))} adjustment`,
      }
    }
    case 'attendance.updated': {
      const recorded = num('Recorded') ?? 0
      return {
        category: 'points',
        text: `Updated headcounts for ${recorded} ${recorded === 1 ? 'team' : 'teams'}`,
      }
    }

    case 'game.created':
      return { category: 'games', text: `Added the game ${text('Name') ?? ''}`.trim() }
    case 'game.updated': {
      const before = nested('Before', 'Name')
      const after = nested('After', 'Name')
      return {
        category: 'games',
        text:
          typeof before === 'string' && typeof after === 'string' && before !== after
            ? `Renamed ${before} to ${after}`
            : `Edited ${typeof after === 'string' ? after : 'a game'}`,
      }
    }
    case 'game.retired':
      return { category: 'games', text: `Retired ${text('Name') ?? 'a game'}` }
    case 'game.restored':
      return { category: 'games', text: `Brought back ${text('Name') ?? 'a game'}` }
    case 'game.deleted':
      return { category: 'games', text: `Deleted the game ${text('Name') ?? ''}`.trim() }
    case 'game.reordered':
      return { category: 'games', text: 'Reordered the games' }

    case 'scoring_profile.created':
      return { category: 'scoring', text: `Created the scoring rules ${text('Name') ?? ''}`.trim() }
    case 'scoring_profile.updated': {
      const after = nested('After', 'Name')
      return { category: 'scoring', text: `Edited ${typeof after === 'string' ? after : 'scoring rules'}` }
    }
    case 'scoring_profile.default_set':
      return { category: 'scoring', text: `Made ${text('Name') ?? 'a set of rules'} the default` }
    case 'scoring_profile.retired':
      return { category: 'scoring', text: `Retired ${text('Name') ?? 'a set of rules'}` }
    case 'scoring_profile.restored':
      return { category: 'scoring', text: `Brought back ${text('Name') ?? 'a set of rules'}` }
    case 'scoring_profile.deleted':
      return { category: 'scoring', text: `Deleted the scoring rules ${text('Name') ?? ''}`.trim() }
    case 'scoring_profile.duplicated':
      return {
        category: 'scoring',
        text: `Copied ${text('From') ?? 'a set of rules'} as ${text('Name') ?? 'a new set'}`,
      }

    case 'person.added':
      return {
        category: 'people',
        text: `Added ${text('Email') ?? 'someone'} as ${article(roleLabel(text('Role')))}`,
      }
    case 'person.role_changed':
      return {
        category: 'people',
        text: `Changed ${text('Email') ?? 'someone'} from ${roleLabel(text('From')) ?? '?'} to ${roleLabel(text('To')) ?? '?'}`,
      }
    case 'person.deactivated':
      return { category: 'people', text: `Deactivated ${text('Email') ?? 'someone'}` }
    case 'person.reactivated':
      return { category: 'people', text: `Reactivated ${text('Email') ?? 'someone'}` }

    default:
      return { category: 'other', text: humanize(entry.action) }
  }
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
