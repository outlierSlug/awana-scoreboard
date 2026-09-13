import { useQuery } from '@tanstack/react-query'
import { ArrowRight, History, MonitorPlay, Radio } from 'lucide-react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { api } from '@/lib/apiClient'
import { queryKeys } from '@/lib/queryClient'
import { formatDate } from '@/lib/format'
import type { SessionSummary } from '@/lib/types'

/**
 * The landing page, and the answer to "which board?".
 *
 * Somebody standing at a laptop with thirty seconds before games start needs
 * one obvious thing to click, so live sessions are the whole page and
 * everything else is secondary.
 */
export function HomePage() {
  // v1 ships configured for one church, so the slug is fixed here rather than
  // routed. It moves into the URL when a second church exists.
  const church = 'church'

  const { data, isPending, error } = useQuery<SessionSummary[]>({
    queryKey: queryKeys.liveSessions(church),
    queryFn: ({ signal }) => api.liveSessions(church, signal),
    // Somebody may leave this open waiting for the night to start.
    refetchInterval: 15_000,
  })

  // Last week's result is the other thing anyone comes here for, and a session
  // leaves the list above the moment it is finished.
  const finished = useQuery<SessionSummary[]>({
    queryKey: queryKeys.finishedSessions(church),
    queryFn: ({ signal }) => api.finishedSessions(church, signal),
  })

  return (
    <div className="min-h-dvh bg-background text-foreground">
      <div className="mx-auto flex min-h-dvh max-w-2xl flex-col px-5 py-10">
        <header className="mb-8">
          <h1 className="text-3xl font-bold tracking-tight">Awana Scoreboard</h1>
          <p className="mt-2 text-muted-foreground">
            Live scores for tonight&rsquo;s games.
          </p>
        </header>

        <main className="flex-1">
          <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold tracking-wide text-muted-foreground uppercase">
            <Radio className="size-4" />
            Live now
          </h2>

          {isPending && <SkeletonCard />}

          {error && (
            <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
              Could not reach the server. The board will reconnect on its own once it is back.
            </p>
          )}

          {data && data.length === 0 && (
            <div className="rounded-xl border border-dashed p-8 text-center">
              <p className="font-medium">Nothing is live right now.</p>
              <p className="mt-1 text-sm text-muted-foreground">
                A board appears here the moment a session starts.
              </p>
            </div>
          )}

          <ul className="flex flex-col gap-3">
            {data?.map((session) => (
              <li key={session.id}>
                <Link
                  to={`/board/${session.slug}`}
                  className="group/card flex items-center gap-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10 transition-all hover:ring-foreground/25"
                >
                  <span className="flex size-11 shrink-0 items-center justify-center rounded-lg bg-muted">
                    <MonitorPlay className="size-5" />
                  </span>

                  <span className="min-w-0 flex-1">
                    <span className="flex items-center gap-2">
                      <span className="text-lg font-semibold">{session.divisionName}</span>
                      <LiveBadge />
                    </span>
                    <span className="mt-0.5 block text-sm text-muted-foreground">
                      {formatDate(session.date)} · {session.roundCount}{' '}
                      {session.roundCount === 1 ? 'round' : 'rounds'}
                    </span>
                  </span>

                  <ArrowRight className="size-5 shrink-0 text-muted-foreground transition-transform group-hover/card:translate-x-0.5" />
                </Link>
              </li>
            ))}
          </ul>

          {data && data.length > 0 && (
            <p className="mt-4 text-sm text-muted-foreground">
              Projecting this? Open a board and add{' '}
              <code className="rounded bg-muted px-1 py-0.5">?tv=1</code> for a full screen with no
              chrome.
            </p>
          )}

          {finished.data && finished.data.length > 0 && (
            <section className="mt-10">
              <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold tracking-wide text-muted-foreground uppercase">
                <History className="size-4" />
                Finished
              </h2>

              <ul className="flex flex-col gap-2">
                {finished.data.map((session) => (
                  <li key={session.id}>
                    <Link
                      to={`/board/${session.slug}`}
                      className="group/card flex items-center gap-3 rounded-xl px-4 py-3 ring-1 ring-foreground/10 transition-all hover:ring-foreground/25"
                    >
                      <span className="min-w-0 flex-1">
                        <span className="font-semibold">{session.divisionName}</span>
                        <span className="mt-0.5 block text-sm text-muted-foreground">
                          {formatDate(session.date)} · {session.roundCount}{' '}
                          {session.roundCount === 1 ? 'round' : 'rounds'}
                        </span>
                      </span>

                      <ArrowRight className="size-4 shrink-0 text-muted-foreground transition-transform group-hover/card:translate-x-0.5" />
                    </Link>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </main>

        <footer className="mt-10 border-t pt-6">
          <Button asChild variant="outline" size="lg">
            <Link to="/app">Scorekeeper console</Link>
          </Button>

          <p className="mt-6 text-xs leading-relaxed text-muted-foreground">
            Awana is a registered trademark of Awana Clubs International. This project is an
            independent tool and is not affiliated with or endorsed by Awana Clubs International.
          </p>
        </footer>
      </div>
    </div>
  )
}

function LiveBadge() {
  return (
    <span
      className="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-bold tracking-wide"
      style={{
        background: 'color-mix(in oklch, var(--color-status-live), transparent 88%)',
        color: 'var(--color-team-green)',
      }}
    >
      <span
        className="size-1.5 rounded-full"
        style={{ background: 'var(--color-status-live)' }}
        aria-hidden
      />
      LIVE
    </span>
  )
}

function SkeletonCard() {
  return (
    <div className="h-[76px] animate-pulse rounded-xl bg-muted" aria-label="Loading live sessions" />
  )
}


