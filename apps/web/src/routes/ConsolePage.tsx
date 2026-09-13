import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, ExternalLink, Play, RotateCcw, Square } from 'lucide-react'
import { Link, useParams } from 'react-router'
import { RoundEntry } from '@/components/round/RoundEntry'
import { Button } from '@/components/ui/button'
import { api, ApiError } from '@/lib/apiClient'
import { queryKeys } from '@/lib/queryClient'
import { SessionStatus, type SessionDetail } from '@/lib/types'
import { formatDate } from '@/lib/format'

export function ConsolePage() {
  const { id } = useParams<{ id: string }>()
  const queryClient = useQueryClient()

  const session = useQuery<SessionDetail>({
    queryKey: queryKeys.session(id ?? ''),
    queryFn: ({ signal }) => api.session(id!, signal),
    enabled: Boolean(id),
  })

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.session(id!) })
    await queryClient.invalidateQueries({ queryKey: queryKeys.sessions() })
  }

  const start = useMutation({ mutationFn: () => api.startSession(id!), onSuccess: refresh })
  const finish = useMutation({ mutationFn: () => api.finishSession(id!), onSuccess: refresh })
  const reopen = useMutation({ mutationFn: () => api.reopenSession(id!), onSuccess: refresh })

  if (session.isPending) {
    return <div className="h-40 animate-pulse rounded-xl bg-muted" />
  }

  if (session.error || !session.data) {
    return (
      <div className="rounded-xl border border-destructive/30 bg-destructive/5 p-6">
        <p className="font-medium">That session could not be loaded.</p>
        <p className="mt-1 text-sm text-muted-foreground">
          {(session.error as Error)?.message}
        </p>
        <Button asChild variant="outline" className="mt-4">
          <Link to="/app/sessions">Back to sessions</Link>
        </Button>
      </div>
    )
  }

  const data = session.data
  const pending = start.isPending || finish.isPending || reopen.isPending
  const error = start.error ?? finish.error ?? reopen.error
  const liveRounds = data.rounds.filter((round) => !round.isVoided)

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Button asChild variant="ghost" size="sm" className="-ml-2.5 mb-2">
          <Link to="/app/sessions">
            <ArrowLeft />
            Sessions
          </Link>
        </Button>

        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">{data.divisionName} Games</h1>
            <p className="mt-1 text-sm text-muted-foreground">
              {formatDate(data.date)} · {liveRounds.length}{' '}
              {liveRounds.length === 1 ? 'round' : 'rounds'} recorded
              {data.rounds.length !== liveRounds.length &&
                `, ${data.rounds.length - liveRounds.length} voided`}
            </p>
          </div>

          <div className="flex flex-wrap gap-2">
            {data.status !== SessionStatus.Setup && (
              <Button asChild variant="outline" size="lg">
                <a href={`/board/${data.slug}`} target="_blank" rel="noreferrer">
                  Open board
                  <ExternalLink />
                </a>
              </Button>
            )}

            {data.status === SessionStatus.Setup && (
              <Button size="lg" disabled={pending} onClick={() => start.mutate()}>
                <Play />
                {start.isPending ? 'Starting...' : 'Start session'}
              </Button>
            )}

            {data.status === SessionStatus.Running && (
              <Button variant="outline" size="lg" disabled={pending} onClick={() => finish.mutate()}>
                <Square />
                Finish
              </Button>
            )}

            {data.status === SessionStatus.Finished && (
              <Button variant="outline" size="lg" disabled={pending} onClick={() => reopen.mutate()}>
                <RotateCcw />
                Reopen
              </Button>
            )}
          </div>
        </div>
      </div>

      {error instanceof ApiError && (
        <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
          {error.message}
        </p>
      )}

      {data.status === SessionStatus.Setup && (
        <div className="rounded-xl border border-dashed p-5">
          <p className="font-medium">Not started yet.</p>
          <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
            Starting locks tonight&rsquo;s scoring rules onto this session. Changing the scoring
            profile later will not alter these results.
          </p>
        </div>
      )}

      {data.status === SessionStatus.Running && (
        <RoundEntry session={data} onRecorded={refresh} />
      )}

      {data.status !== SessionStatus.Running && (
      <section>
        <h2 className="mb-3 text-sm font-semibold tracking-wide text-muted-foreground uppercase">
          Teams
        </h2>
        <ul className="grid grid-cols-2 gap-2 sm:grid-cols-4">
          {data.teams.map((team) => (
            <li
              key={team.teamId}
              className="flex h-20 flex-col items-center justify-center gap-1 rounded-xl font-semibold"
              style={{ background: team.colorHex, color: team.textOnColorHex }}
            >
              <span className="text-lg">{team.name}</span>
              {team.headcount !== null && (
                <span className="text-xs opacity-80">{team.headcount} present</span>
              )}
            </li>
          ))}
        </ul>
      </section>
      )}

      <section>
        <h2 className="mb-3 text-sm font-semibold tracking-wide text-muted-foreground uppercase">
          Rounds
        </h2>

        {data.rounds.length === 0 ? (
          <div className="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground">
            No rounds recorded yet.
          </div>
        ) : (
          <ul className="flex flex-col gap-2">
            {data.rounds.map((round) => (
              <li
                key={round.id}
                className={[
                  'rounded-xl p-4 ring-1',
                  round.isVoided ? 'bg-muted/50 ring-transparent' : 'bg-card ring-foreground/10',
                ].join(' ')}
              >
                <div className="flex items-baseline justify-between gap-3">
                  <span
                    className={[
                      'font-semibold',
                      round.isVoided ? 'text-muted-foreground line-through' : '',
                    ].join(' ')}
                  >
                    Round {round.roundNumber} · {round.gameName}
                  </span>
                  <span className="text-xs text-muted-foreground">
                    {round.isVoided ? `Voided · ${round.voidReason}` : null}
                    {!round.isVoided && round.multiplier !== 1 ? `×${round.multiplier}` : null}
                  </span>
                </div>

                {!round.isVoided && (
                  <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
                    {round.teams.map((team) => (
                      <span key={team.teamId}>
                        {team.teamName}{' '}
                        <span className="font-semibold text-foreground tabular-nums">
                          {Math.round(team.points)}
                        </span>
                        {team.isDisqualified ? ' (DQ)' : ''}
                      </span>
                    ))}
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>


    </div>
  )
}
