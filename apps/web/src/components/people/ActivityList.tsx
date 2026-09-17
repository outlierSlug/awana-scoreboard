import { useInfiniteQuery } from '@tanstack/react-query'
import {
  CalendarClock,
  Calculator,
  CirclePlus,
  Dot,
  Gamepad2,
  Settings2,
  Trophy,
  UserCog,
  type LucideIcon,
} from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { describeActivity, groupActivity, type ActivityCategory, type ActivityGroup } from '@/lib/activity'
import { api } from '@/lib/apiClient'
import { formatDate } from '@/lib/format'
import { queryKeys } from '@/lib/queryClient'
import type { ActivityEntry, ActivityPage } from '@/lib/types'

const ICONS: Record<ActivityCategory, LucideIcon> = {
  rounds: Trophy,
  sessions: CalendarClock,
  points: CirclePlus,
  games: Gamepad2,
  scoring: Calculator,
  people: UserCog,
  other: Dot,
}

/**
 * How many of a group's entries show before it folds. A night is thirty or more
 * lines of rounds, and the question is usually about the last few.
 */
const FOLDED = 6

/**
 * Everything anyone has changed, a night at a time.
 *
 * The question this answers is usually specific and asked days later: why
 * does Blue have 145, who cleared round 6, when did Sam become an admin. So a
 * night's entries sit together under that night, and each line says who and
 * when without having to open anything.
 */
export function ActivityList() {
  const activity = useInfiniteQuery<ActivityPage>({
    queryKey: queryKeys.activity(),
    queryFn: ({ pageParam, signal }) => api.activity(pageParam as string | null, signal),
    initialPageParam: null,
    getNextPageParam: (last) => last.nextBefore,
  })

  if (activity.isPending) return <div className="h-40 animate-pulse rounded-xl bg-muted" />

  if (activity.error) {
    return (
      <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
        Could not load activity. {(activity.error as Error).message}
      </p>
    )
  }

  const entries = activity.data.pages.flatMap((page) => page.entries)

  // Merged across pages: an older page can mention a game the newer ones do not.
  const names = Object.assign({}, ...activity.data.pages.map((page) => page.names)) as Record<string, string>

  if (entries.length === 0) {
    return (
      <div className="rounded-xl border border-dashed p-10 text-center">
        <p className="font-medium">Nothing has happened yet.</p>
        <p className="mt-1 text-sm text-muted-foreground">
          Rounds, sessions and changes to people show up here as they are made.
        </p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      {groupActivity(entries).map((group) => (
        <Group key={group.key} group={group} names={names} />
      ))}

      {activity.hasNextPage && (
        <Button
          variant="outline"
          className="self-center"
          disabled={activity.isFetchingNextPage}
          onClick={() => activity.fetchNextPage()}
        >
          {activity.isFetchingNextPage ? 'Loading...' : 'Show older'}
        </Button>
      )}
    </div>
  )
}

function Group({ group, names }: { group: ActivityGroup; names: Record<string, string> }) {
  const [open, setOpen] = useState(false)

  const hidden = group.entries.length - FOLDED
  const shown = open || hidden <= 0 ? group.entries : group.entries.slice(0, FOLDED)

  return (
    <section>
      <header className="mb-2 flex items-baseline justify-between gap-3">
        <div className="min-w-0">
          {group.session ? (
            <h2 className="flex flex-wrap items-baseline gap-x-2">
              <span className="font-semibold">{group.session.divisionName}</span>
              <span className="text-sm text-muted-foreground">{formatDate(group.session.date)}</span>
            </h2>
          ) : (
            <h2 className="flex flex-wrap items-center gap-x-2">
              <Settings2 className="size-4 text-muted-foreground" />
              <span className="font-semibold">Games, scoring and people</span>
              <span className="text-sm text-muted-foreground">{group.day}</span>
            </h2>
          )}
        </div>

        <span className="shrink-0 text-xs text-muted-foreground tabular-nums">
          {group.entries.length} {group.entries.length === 1 ? 'change' : 'changes'}
        </span>
      </header>

      <ul className="flex flex-col divide-y rounded-xl bg-card ring-1 ring-foreground/10">
        {shown.map((entry) => (
          <Row key={entry.id} entry={entry} names={names} withDay={group.spansDays} />
        ))}

        {hidden > 0 && (
          <li>
            <button
              type="button"
              onClick={() => setOpen(!open)}
              className="w-full px-4 py-2.5 text-left text-sm font-medium text-muted-foreground transition-colors hover:text-foreground"
            >
              {open ? 'Show fewer' : `Show ${hidden} more`}
            </button>
          </li>
        )}
      </ul>
    </section>
  )
}

function Row({
  entry,
  names,
  withDay,
}: {
  entry: ActivityEntry
  names: Record<string, string>
  withDay: boolean
}) {
  const line = describeActivity(entry, names)
  const Icon = ICONS[line.category]

  // A night set up on Tuesday and played on Friday needs the day on each line;
  // one played in an evening does not, and the time alone reads faster.
  const when = new Date(entry.at).toLocaleString(
    undefined,
    withDay
      ? { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }
      : { hour: 'numeric', minute: '2-digit' },
  )

  return (
    <li className="flex gap-3 px-4 py-3">
      <span className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full bg-muted">
        <Icon className="size-3.5 text-muted-foreground" />
      </span>

      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium wrap-break-word">{line.text}</p>

        {line.details.map((detail) => (
          <p key={detail} className="mt-0.5 text-sm wrap-break-word text-muted-foreground">
            {detail}
          </p>
        ))}

        <p className="mt-1 text-xs text-muted-foreground">
          {/* No actor means the system did it on its own, seeding for instance.
              Saying so beats a blank that reads like a missing name. */}
          {entry.actorName ?? 'System'} · {when}
        </p>
      </div>
    </li>
  )
}
