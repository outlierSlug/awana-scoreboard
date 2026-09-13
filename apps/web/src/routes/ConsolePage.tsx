import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, ExternalLink, Flag, Play, RotateCcw } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { FinalStandings } from '@/components/round/FinalStandings'
import { RoundEntry } from '@/components/round/RoundEntry'
import { RoundHistory } from '@/components/round/RoundHistory'
import { SessionStatusLabel } from '@/components/SessionStatusLabel'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { Modal } from '@/components/ui/Modal'
import { api, ApiError } from '@/lib/apiClient'
import { queryKeys } from '@/lib/queryClient'
import { SessionStatus, type RoundSummary, type Scoreboard, type SessionDetail } from '@/lib/types'
import { formatDate } from '@/lib/format'

export function ConsolePage() {
  const { id } = useParams<{ id: string }>()
  const queryClient = useQueryClient()

  // The round open in the correction dialog. It is saved over in place when
  // the dialog is submitted, and closing the dialog changes nothing.
  const [editing, setEditing] = useState<{ round: RoundSummary; label: string } | null>(null)
  const [finishing, setFinishing] = useState(false)

  // A tie takes over the whole page, not just the form. Everything that is not
  // a team block steps back and stops answering until the tie is closed, so
  // the one thing left to do is the only thing that looks live.
  const [tieArmed, setTieArmed] = useState(false)
  const recede = tieArmed
    ? 'pointer-events-none opacity-40 transition-opacity duration-200'
    : 'transition-opacity duration-200'

  const session = useQuery<SessionDetail>({
    queryKey: queryKeys.session(id ?? ''),
    queryFn: ({ signal }) => api.session(id!, signal),
    enabled: Boolean(id),
  })

  // Once the night is over the scorekeeper wants the result, not a list of who
  // played. Read from the same endpoint the wall reads, so the totals here and
  // the totals in the room are the same numbers.
  const finalBoard = useQuery<Scoreboard>({
    queryKey: queryKeys.scoreboard(session.data?.slug ?? ''),
    queryFn: ({ signal }) => api.publicScoreboard(session.data!.slug, signal),
    enabled: session.data?.status === SessionStatus.Finished,
  })

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.session(id!) })
    await queryClient.invalidateQueries({ queryKey: queryKeys.sessions() })

    // The standings panel reads the board's own endpoint, so a round recorded
    // or cleared here has to reach it too.
    if (session.data) {
      await queryClient.invalidateQueries({ queryKey: queryKeys.scoreboard(session.data.slug) })
    }
  }

  const clearRound = useMutation({
    mutationFn: ({ round, reason }: { round: RoundSummary; reason: string }) =>
      api.voidRound(round.id, reason),
    onSuccess: refresh,
  })

  const start = useMutation({ mutationFn: () => api.startSession(id!), onSuccess: refresh })
  const finish = useMutation({
    mutationFn: () => api.finishSession(id!),
    onSuccess: async () => {
      setFinishing(false)
      await refresh()
    },
  })
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
  const roundPending = clearRound.isPending
  const error = start.error ?? finish.error ?? reopen.error ?? clearRound.error
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
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <h1 className="text-2xl font-bold tracking-tight">{data.divisionName} Games</h1>
              {/* Said outright rather than left to be inferred from which
                  buttons happen to be on screen. */}
              <SessionStatusLabel status={data.status} />
            </div>
            <p className="mt-1 text-sm text-muted-foreground">
              {formatDate(data.date)} · {liveRounds.length}{' '}
              {liveRounds.length === 1 ? 'round' : 'rounds'} recorded
            </p>
          </div>

          <div className={`flex flex-wrap gap-2 ${recede}`}>
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
              <Button variant="outline" size="lg" disabled={pending} onClick={() => setFinishing(true)}>
                <Flag />
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
        <RoundEntry
          session={data}
          // The dialog on top gets the keyboard while it is open.
          shortcuts={editing === null}
          onTieArmed={setTieArmed}
          onDone={refresh}
        />
      )}

      {data.status === SessionStatus.Finished && (
        <section>
          <h2 className="mb-3 text-sm font-semibold tracking-wide text-muted-foreground uppercase">
            Final standings
          </h2>

          {finalBoard.data ? (
            <FinalStandings standings={finalBoard.data.standings} />
          ) : (
            <div className="h-24 animate-pulse rounded-xl bg-muted" />
          )}
        </section>
      )}

      {data.status === SessionStatus.Setup && (
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

      <div className={recede}>
        <RoundHistory
          rounds={data.rounds}
          teams={data.teams}
          editable={data.status === SessionStatus.Running}
          busy={roundPending}
          onClear={(round, reason) => clearRound.mutate({ round, reason })}
          onEdit={(round, label) => setEditing({ round, label })}
        />
      </div>

      <ConfirmDialog
        open={finishing}
        title="Finish this session?"
        confirmLabel="Finish session"
        busy={finish.isPending}
        onCancel={() => setFinishing(false)}
        onConfirm={() => finish.mutate()}
      >
        <p>
          The board stops taking rounds and shows tonight&rsquo;s final standings. You can reopen
          the session afterwards if there is another round to play.
        </p>
      </ConfirmDialog>

      <Modal
        open={editing !== null}
        onClose={() => setEditing(null)}
        className="w-[min(64rem,calc(100%-2rem))]"
      >
        <div className="p-5">
          {editing && (
            <RoundEntry
              session={data}
              editing={editing.round}
              editingLabel={editing.label}
              onDone={async () => {
                setEditing(null)
                await refresh()
              }}
              onCancel={() => setEditing(null)}
            />
          )}
        </div>
      </Modal>

    </div>
  )
}
