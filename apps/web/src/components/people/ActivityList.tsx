import { useInfiniteQuery } from '@tanstack/react-query'
import {
  CalendarClock,
  Calculator,
  CirclePlus,
  Dot,
  Gamepad2,
  Trophy,
  UserCog,
  type LucideIcon,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { describeActivity, type ActivityCategory } from '@/lib/activity'
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
 * Everything anyone has changed, newest first.
 *
 * The question this answers is usually specific and asked days later: why
 * does Blue have 145, who cleared round 6, when did Sam become an admin. So
 * every line says who, when, and on which night, without opening anything.
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
      {groupByDay(entries).map(({ day, items }) => (
        <section key={day}>
          <h2 className="mb-2 text-xs font-semibold tracking-wide text-muted-foreground uppercase">{day}</h2>

          <ul className="flex flex-col divide-y rounded-xl bg-card ring-1 ring-foreground/10">
            {items.map((entry) => (
              <ActivityRow key={entry.id} entry={entry} names={names} />
            ))}
          </ul>
        </section>
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

function ActivityRow({ entry, names }: { entry: ActivityEntry; names: Record<string, string> }) {
  const line = describeActivity(entry, names)
  const Icon = ICONS[line.category]

  const time = new Date(entry.at).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })

  return (
    <li className="flex gap-3 px-4 py-3">
      <span className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full bg-muted">
        <Icon className="size-3.5 text-muted-foreground" />
      </span>

      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium break-words">{line.text}</p>

        {line.detail && <p className="mt-0.5 text-sm text-muted-foreground break-words">&ldquo;{line.detail}&rdquo;</p>}

        <p className="mt-0.5 text-xs text-muted-foreground">
          {/* The system did it on its own when there is no actor: seeding, for
              instance. Saying so beats a blank that reads like a missing name. */}
          {entry.actorName ?? 'System'} · {time}
          {entry.session && (
            <>
              {' '}
              · {entry.session.divisionName}, {formatDate(entry.session.date)}
            </>
          )}
        </p>
      </div>
    </li>
  )
}

/**
 * Headed by the local day the change was made, which is what somebody means by
 * "on Friday". The session a round belongs to is a separate thing, and each
 * line names it.
 */
function groupByDay(entries: ActivityEntry[]): { day: string; items: ActivityEntry[] }[] {
  const groups: { day: string; items: ActivityEntry[] }[] = []

  for (const entry of entries) {
    const day = new Date(entry.at).toLocaleDateString(undefined, {
      weekday: 'long',
      month: 'long',
      day: 'numeric',
      year: 'numeric',
    })

    const last = groups.at(-1)
    if (last?.day === day) last.items.push(entry)
    else groups.push({ day, items: [entry] })
  }

  return groups
}
