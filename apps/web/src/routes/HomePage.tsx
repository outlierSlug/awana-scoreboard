import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { ArrowRight, ClipboardList, History, Maximize2, MonitorPlay, Radio } from 'lucide-react'
import { Link } from 'react-router'
import { ThemeToggle } from '@/components/ThemeToggle'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import { useMe } from '@/lib/auth'
import { api } from '@/lib/apiClient'
import { useDocumentTitle } from '@/lib/hooks/useDocumentTitle'
import { queryKeys } from '@/lib/queryClient'
import { useHub } from '@/lib/signalr/hubContext'
import { formatDate } from '@/lib/format'
import type { SessionSummary } from '@/lib/types'

/**
 * Nights shown under Recent results before the rest move to the archive.
 *
 * Nights rather than sessions, so a Friday is never split with Sparks on the
 * page and T&T in the archive. Two covers "tonight" and "last week", which is
 * what almost everybody opening this is looking for.
 */
const RECENT_NIGHTS = 2

/** Enough for several seasons. The API clamps it to the same figure. */
const ARCHIVE_LIMIT = 200

/**
 * The landing page, and the answer to "which board?".
 *
 * Somebody standing at a laptop with thirty seconds before games start needs
 * one obvious thing to click, so live sessions lead the page and everything
 * else is secondary. Past results are kept short on purpose: a season adds
 * two sessions a week, and a page that grows all year stops being a landing
 * page.
 */
export function HomePage() {
  // v1 ships configured for one church, so the slug is fixed here rather than
  // routed. It moves into the URL when a second church exists.
  const church = 'church'

  useDocumentTitle(null)

  const { isSignedIn } = useMe()
  const [archiveOpen, setArchiveOpen] = useState(false)

  const live = useQuery<SessionSummary[]>({
    queryKey: queryKeys.liveSessions(church),
    queryFn: ({ signal }) => api.liveSessions(church, signal),
    // Somebody may leave this open waiting for the night to start.
    refetchInterval: 15_000,

    // And "leave this open" usually means on a second screen, or a tab that is
    // not the one being looked at. Polling stops in an unfocused window by
    // default, which turns the wait for the night to start into a page that
    // quietly froze at "nothing is live right now".
    refetchIntervalInBackground: true,
  })

  // One query for both the recent list and the archive, so the real-time
  // invalidation that moves a session here the moment it finishes keeps both
  // current. A few seasons of rows is a few kilobytes.
  const finished = useQuery<SessionSummary[]>({
    queryKey: queryKeys.finishedSessions(church),
    queryFn: ({ signal }) => api.finishedSessions(church, ARCHIVE_LIMIT, signal),

    // This list is the other half of finishing a session. Slower than the live
    // list on purpose: a session finishes once a night, and the push below is
    // what makes it feel immediate. This is only the backstop for a screen
    // whose connection is down.
    refetchInterval: 60_000,
    refetchIntervalInBackground: true,
  })

  // Watch whatever is live, so the page hears about it rather than waiting for
  // the next poll. Joining each live session's group is what the board already
  // does; the pushes land as invalidations, so both lists above re-read.
  const { join, leave } = useHub()
  const liveIds = (live.data ?? []).map((session) => session.id).join(',')

  useEffect(() => {
    if (!liveIds) return

    const ids = liveIds.split(',')
    for (const id of ids) join(id)
    return () => {
      for (const id of ids) leave(id)
    }
  }, [liveIds, join, leave])

  const nights = groupByNight(finished.data ?? [])
  const recent = nights.slice(0, RECENT_NIGHTS)

  return (
    <div className="flex min-h-dvh flex-col bg-background text-foreground">
      <header className="border-b">
        <div className="mx-auto flex h-14 max-w-3xl items-center gap-3 px-4 sm:px-6">
          <Link to="/" className="flex min-w-0 items-center gap-2 font-semibold">
            <ClipboardList className="size-5 shrink-0" />
            <span className="truncate">Awana Scoreboard</span>
          </Link>

          <div className="ml-auto flex shrink-0 items-center gap-1.5 sm:gap-2">
            <ThemeToggle />

            {/* Up here rather than at the foot of the page: the people who
                need it know to look for it, and everyone else can ignore it. */}
            <Button asChild variant="outline" size="sm">
              <Link to="/app">
                {isSignedIn ? (
                  'Open console'
                ) : (
                  <>
                    <span className="sm:hidden">Sign in</span>
                    <span className="hidden sm:inline">Scorekeeper sign in</span>
                  </>
                )}
              </Link>
            </Button>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-3xl flex-1 px-4 py-8 sm:px-6 sm:py-12">
        {/* No page heading above this on purpose. The header already says what
            the site is, and a hero restating it pushed the one thing anybody
            came for, tonight's board, further down a phone screen. The page
            still has an h1 for screen readers, just not a visible one. */}
        <h1 className="sr-only">Awana Scoreboard</h1>

        <section aria-labelledby="live-heading">
          <SectionHeading id="live-heading" Icon={Radio}>
            Live now
          </SectionHeading>

          {live.isPending && <div className="h-[84px] animate-pulse rounded-xl bg-muted" aria-label="Loading" />}

          {live.error && (
            <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
              Could not reach the server. This page will reconnect on its own once it is back.
            </p>
          )}

          {live.data?.length === 0 && (
            <div className="flex flex-col items-center rounded-xl border border-dashed px-6 py-10 text-center">
              <span className="flex size-10 items-center justify-center rounded-full bg-muted">
                <MonitorPlay className="size-5 text-muted-foreground" />
              </span>
              <p className="mt-3 font-medium">No games are live right now</p>
              <p className="mt-1 text-sm text-muted-foreground">
                A board appears here as soon as a session starts.
              </p>
            </div>
          )}

          {live.data && live.data.length > 0 && (
            <ul className="flex flex-col gap-3">
              {live.data.map((session) => (
                <LiveCard key={session.id} session={session} />
              ))}
            </ul>
          )}
        </section>

        {recent.length > 0 && (
          <section className="mt-12" aria-labelledby="results-heading">
            <div className="mb-3 flex items-center justify-between gap-3">
              <SectionHeading id="results-heading" Icon={History} className="mb-0">
                Recent results
              </SectionHeading>

              {nights.length > RECENT_NIGHTS && (
                <Button variant="ghost" size="sm" className="-my-1 -mr-2.5" onClick={() => setArchiveOpen(true)}>
                  All results
                  <ArrowRight />
                </Button>
              )}
            </div>

            <div className="flex flex-col gap-6">
              {recent.map((night) => (
                <Night key={night.date} night={night} />
              ))}
            </div>
          </section>
        )}
      </main>

      <footer className="border-t">
        <p className="mx-auto max-w-3xl px-4 py-6 text-xs leading-relaxed text-muted-foreground sm:px-6">
          Awana is a registered trademark of Awana Clubs International. This project is an
          independent tool and is not affiliated with or endorsed by Awana Clubs International.
        </p>
      </footer>

      <Modal open={archiveOpen} onClose={() => setArchiveOpen(false)} className="w-[min(36rem,calc(100%-2rem))]">
        <div className="flex flex-col gap-5 p-5">
          <div>
            <h2 className="text-lg font-bold tracking-tight">All results</h2>
            <p className="mt-0.5 text-sm text-muted-foreground">
              {nights.length} {nights.length === 1 ? 'night' : 'nights'}, newest first
            </p>
          </div>

          <div className="flex flex-col gap-6">
            {nights.map((night) => (
              <Night key={night.date} night={night} />
            ))}
          </div>

          <Button variant="outline" size="lg" onClick={() => setArchiveOpen(false)}>
            Close
          </Button>
        </div>
      </Modal>
    </div>
  )
}

function SectionHeading({
  id,
  Icon,
  className = 'mb-3',
  children,
}: {
  id: string
  Icon: typeof Radio
  className?: string
  children: React.ReactNode
}) {
  return (
    <h2
      id={id}
      className={`flex items-center gap-2 text-xs font-semibold tracking-wide text-muted-foreground uppercase ${className}`}
    >
      <Icon className="size-4" />
      {children}
    </h2>
  )
}

/**
 * A live session, and the two ways anyone opens one.
 *
 * The whole card opens the board, which is what a parent on a phone wants. The
 * second button opens it full screen for a projector, which used to be a tip
 * explaining a URL parameter to whoever was setting up the TV.
 */
function LiveCard({ session }: { session: SessionSummary }) {
  return (
    <li className="group/card relative flex items-center gap-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10 transition-shadow hover:ring-foreground/25">
      <span className="flex size-12 shrink-0 items-center justify-center rounded-lg bg-muted">
        <MonitorPlay className="size-5" />
      </span>

      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          {/* Stretched over the whole card, so the card is one target without a
              link nested inside another link.

              Raised with a z-index, which is load bearing. The arrow slides on
              hover, and a transformed element paints above an unraised
              overlay, so with a real mouse the arrow was always on top of the
              link exactly when somebody clicked it, and nothing happened. The
              Full screen button sits above this on purpose. */}
          <Link
            to={`/board/${session.slug}`}
            className="truncate text-lg font-semibold after:absolute after:inset-0 after:z-[1] after:rounded-xl focus-visible:outline-none focus-visible:after:ring-2 focus-visible:after:ring-ring"
          >
            {session.divisionName}
          </Link>
          <LiveBadge />
        </div>
        <p className="mt-0.5 text-sm text-muted-foreground">
          {formatDate(session.date)} · {rounds(session.roundCount)}
        </p>
      </div>

      <Button asChild variant="outline" size="sm" className="relative z-10 hidden sm:inline-flex">
        <Link to={`/board/${session.slug}?tv=1`} title="Full screen, for a projector or TV">
          <Maximize2 />
          Full screen
        </Link>
      </Button>

      <ArrowRight className="size-5 shrink-0 text-muted-foreground transition-transform group-hover/card:translate-x-0.5" />
    </li>
  )
}

interface NightGroup {
  date: string
  sessions: SessionSummary[]
}

/** One card per Friday, one row per division, since that is how a night is remembered. */
function Night({ night }: { night: NightGroup }) {
  return (
    <div>
      <h3 className="mb-2 text-sm font-medium text-muted-foreground">{formatDate(night.date)}</h3>

      <ul className="flex flex-col divide-y rounded-xl bg-card ring-1 ring-foreground/10">
        {night.sessions.map((session) => (
          <li key={session.id}>
            <Link
              to={`/board/${session.slug}`}
              className="group/row flex items-center gap-3 px-4 py-3 transition-colors first:rounded-t-xl last:rounded-b-xl hover:bg-muted/50"
            >
              <span className="min-w-0 flex-1 truncate font-semibold">{session.divisionName}</span>
              <span className="shrink-0 text-sm text-muted-foreground tabular-nums">
                {rounds(session.roundCount)}
              </span>
              <ArrowRight className="size-4 shrink-0 text-muted-foreground transition-transform group-hover/row:translate-x-0.5" />
            </Link>
          </li>
        ))}
      </ul>
    </div>
  )
}

/** Keeps the API's newest-first order for nights, and the division order within one. */
function groupByNight(sessions: SessionSummary[]): NightGroup[] {
  const nights: NightGroup[] = []

  for (const session of sessions) {
    const last = nights.at(-1)
    if (last?.date === session.date) last.sessions.push(session)
    else nights.push({ date: session.date, sessions: [session] })
  }

  return nights
}

function rounds(count: number): string {
  return `${count} ${count === 1 ? 'round' : 'rounds'}`
}

function LiveBadge() {
  return (
    <span
      className="inline-flex shrink-0 items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-bold tracking-wide"
      style={{
        background: 'color-mix(in oklch, var(--color-status-live), transparent 88%)',
        color: 'var(--color-team-green)',
      }}
    >
      <span
        className="size-1.5 animate-pulse rounded-full motion-reduce:animate-none"
        style={{ background: 'var(--color-status-live)' }}
        aria-hidden
      />
      LIVE
    </span>
  )
}
