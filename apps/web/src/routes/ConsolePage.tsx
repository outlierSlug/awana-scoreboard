import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, ExternalLink, Flag, Info, Play, RotateCcw, Users } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ErrorBoundary } from '@/components/ErrorBoundary'
import { FinalStandings } from '@/components/round/FinalStandings'
import { TeamsDialog } from '@/components/round/TeamsDialog'
import { RoundEntry } from '@/components/round/RoundEntry'
import { RoundHistory } from '@/components/round/RoundHistory'
import { SessionsHelpDialog } from '@/components/round/SessionsHelpDialog'
import { SessionStatusLabel } from '@/components/SessionStatusLabel'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { Modal } from '@/components/ui/Modal'
import { useMe } from '@/lib/auth'
import { api, ApiError } from '@/lib/apiClient'
import { queryKeys } from '@/lib/queryClient'
import { useHub } from '@/lib/signalr/hubContext'
import type { ScoringProfile } from '@/lib/types'
import {
  SessionStatus,
  UserRole,
  type Adjustment,
  type RoundSummary,
  type Scoreboard,
  type SessionDetail,
} from '@/lib/types'
import { formatDate } from '@/lib/format'

export function ConsolePage() {
  const { id } = useParams<{ id: string }>()
  const queryClient = useQueryClient()

  // The round open in the correction dialog. It is saved over in place when
  // the dialog is submitted, and closing the dialog changes nothing.
  const [editing, setEditing] = useState<{ round: RoundSummary; label: string } | null>(null)
  const [finishing, setFinishing] = useState(false)
  const [helping, setHelping] = useState(false)
  const [teamsOpen, setTeamsOpen] = useState(false)

  // A tie takes over the whole page, not just the form. Everything that is not
  // a team block steps back and stops answering until the tie is closed, so
  // the one thing left to do is the only thing that looks live.
  const [tieArmed, setTieArmed] = useState(false)

  // Each of these matches a policy on the endpoint behind it. The server is
  // still the gate; this is so nobody is offered a button that cannot work.
  const { can } = useMe()
  const canScore = can(UserRole.Scorekeeper)
  const canRunSession = can(UserRole.GamesLeader)
  const canReopen = can(UserRole.GamesLeader)
  const recede = tieArmed
    ? 'pointer-events-none opacity-40 transition-opacity duration-200'
    : 'transition-opacity duration-200'

  const session = useQuery<SessionDetail>({
    queryKey: queryKeys.session(id ?? ''),
    queryFn: ({ signal }) => api.session(id!, signal),
    enabled: Boolean(id),
  })

  // The console listens to the same session the board does.
  //
  // Not for the scoreboard itself, which this page reads from its own query,
  // but for the news that something changed. Two leaders on two phones is the
  // normal case, and without this each one only ever saw its own edits.
  //
  // Joining by id rather than slug on purpose: the hub accepts either, and the
  // id is what this route already has before the session has loaded.
  const { join, leave } = useHub()

  useEffect(() => {
    if (!id) return
    join(id)
    return () => leave(id)
  }, [id, join, leave])

  // Once the night is over the scorekeeper wants the result, not a list of who
  // played. Read from the same endpoint the wall reads, so the totals here and
  // the totals in the room are the same numbers.
  const finalBoard = useQuery<Scoreboard>({
    queryKey: queryKeys.scoreboard(session.data?.slug ?? ''),
    queryFn: ({ signal }) => api.publicScoreboard(session.data!.slug, signal),
    enabled: session.data?.status === SessionStatus.Finished,
  })

  // Only while the session is still in setup and this person could change it.
  const scoringProfiles = useQuery<ScoringProfile[]>({
    queryKey: queryKeys.scoringProfiles(),
    queryFn: ({ signal }) => api.scoringProfiles(signal),
    enabled: session.data?.status === SessionStatus.Setup && canRunSession,
    staleTime: 5 * 60_000,
  })

  const setScoring = useMutation({
    mutationFn: (scoringProfileId: string | null) => api.setSessionScoring(id!, scoringProfileId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.session(id!) })
    },
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

  // Each of these applies on its own, so the dialog has nothing to save.
  const addAdjustment = useMutation({
    mutationFn: ({ teamId, points, reason }: { teamId: string; points: number; reason: string }) =>
      api.addAdjustment(id!, { teamId, points, reason }),
    onSuccess: refresh,
  })

  const clearAdjustment = useMutation({
    mutationFn: (adjustment: Adjustment) => api.voidAdjustment(adjustment.id),
    onSuccess: refresh,
  })

  const saveHeadcount = useMutation({
    mutationFn: (team: { teamId: string; headcount: number | null }) =>
      api.updateAttendance(id!, { teams: [team] }),
    onSuccess: refresh,
  })

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

  // Only when there is nothing to fall back to. A refetch that fails while the
  // console is already open must not replace it: the scorekeeper may be several
  // taps into a round, and throwing the screen away to report a failed
  // background request would lose the round along with it. A stale console
  // still records; the next write is what surfaces a genuine problem.
  if (!session.data) {
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
  const teamsPending =
    addAdjustment.isPending || clearAdjustment.isPending || saveHeadcount.isPending
  const error =
    start.error ??
    finish.error ??
    reopen.error ??
    clearRound.error ??
    addAdjustment.error ??
    clearAdjustment.error ??
    saveHeadcount.error
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
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <h1 className="text-2xl font-bold tracking-tight">{data.divisionName} Games</h1>

              {/* Said outright rather than left to be inferred from which
                  buttons happen to be on screen. */}
              <SessionStatusLabel status={data.status} />

              {/* Reachable from the screen it describes, rather than only from
                  the list somebody has already navigated away from. */}
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="How sessions work"
                onClick={() => setHelping(true)}
              >
                <Info />
              </Button>
            </div>

            {/* One line rather than three. Date, count and rules are all the
                same kind of fact about tonight, and stacking them made the
                header taller than the thing it labels. */}
            <p className="mt-0.5 flex flex-wrap items-center gap-x-2 text-sm text-muted-foreground">
              <span>{formatDate(data.date)}</span>

              <span aria-hidden>·</span>
              <span>
                {liveRounds.length} {liveRounds.length === 1 ? 'round' : 'rounds'}
              </span>

              <span aria-hidden>·</span>
              <span
                title={
                  data.scoring.isFixed
                    ? 'Fixed when this session started'
                    : 'Fixed when the session starts'
                }
              >
                <span className="font-medium text-foreground">{data.scoring.name}</span>
                <span className="tabular-nums"> {data.scoring.placePoints.join('/')}</span>
              </span>
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

            {/* Occasional, so behind a button rather than permanent furniture
                around the screen that gets used every few minutes. Shown on a
                finished session too: the adjustments are part of how those
                final standings were arrived at, and hiding them afterwards
                leaves a total nobody can account for. */}
            <Button variant="outline" size="lg" onClick={() => setTeamsOpen(true)}>
              <Users />
              Teams
            </Button>

            {data.status === SessionStatus.Setup && canRunSession && (
              <Button size="lg" disabled={pending} onClick={() => start.mutate()}>
                <Play />
                {start.isPending ? 'Starting...' : 'Start session'}
              </Button>
            )}

            {data.status === SessionStatus.Running && canRunSession && (
              <Button variant="outline" size="lg" disabled={pending} onClick={() => setFinishing(true)}>
                <Flag />
                Finish
              </Button>
            )}

            {data.status === SessionStatus.Finished && canReopen && (
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

          {/* The last moment these can be changed, offered where that is said.
              Only when there is more than one set to choose between, matching
              the new session form. */}
          {canRunSession && (scoringProfiles.data ?? []).filter((p) => p.isActive).length > 1 && (
            <div className="mt-4">
              <span className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                Scoring
              </span>

              <div className="flex flex-wrap gap-2">
                {(scoringProfiles.data ?? [])
                  .filter((profile) => profile.isActive)
                  .map((profile) => {
                    // The session reports the default by name, so a set that
                    // IS the default matches either way it was arrived at.
                    const selected = data.scoring.isDefault
                      ? profile.isDefault
                      : data.scoring.name === profile.name

                    return (
                      <button
                        key={profile.id}
                        type="button"
                        disabled={setScoring.isPending}
                        aria-pressed={selected}
                        onClick={() => setScoring.mutate(profile.isDefault ? null : profile.id)}
                        className={[
                          'h-10 rounded-lg border px-3 text-sm font-semibold transition-colors',
                          selected
                            ? 'border-foreground bg-primary text-primary-foreground'
                            : 'border-border bg-background hover:bg-muted',
                        ].join(' ')}
                      >
                        {profile.name}
                      </button>
                    )
                  })}
              </div>

              {setScoring.error instanceof ApiError && (
                <p className="mt-2 text-sm text-destructive">{setScoring.error.message}</p>
              )}
            </div>
          )}
        </div>
      )}

      {/* Signed in, but not to do this. Without saying so the page is just
          empty between the header and the rounds, which reads as broken. */}
      {data.status === SessionStatus.Running && !canScore && (
        <div className="rounded-xl border border-dashed p-5">
          <p className="font-medium">Watching, not recording.</p>
          <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
            Recording rounds needs the scorekeeper role. Ask an admin to change yours, then sign
            out and back in for it to take effect.
          </p>
        </div>
      )}

      {data.status === SessionStatus.Running && canScore && (
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
        <ErrorBoundary label="The rounds could not be shown">
          <RoundHistory
            rounds={data.rounds}
            teams={data.teams}
            editable={data.status === SessionStatus.Running && canScore}
            busy={roundPending}
            onClear={(round, reason) => clearRound.mutate({ round, reason })}
            onEdit={(round, label) => setEditing({ round, label })}
          />
        </ErrorBoundary>
      </div>

      <SessionsHelpDialog open={helping} onClose={() => setHelping(false)} />

      <TeamsDialog
        open={teamsOpen}
        onClose={() => setTeamsOpen(false)}
        teams={data.teams}
        adjustments={data.adjustments}
        canCount={data.status !== SessionStatus.Finished && canScore}
        canAdjust={data.status === SessionStatus.Running && canScore}
        note={
          data.status === SessionStatus.Finished
            ? canReopen
              ? 'This session is finished. Reopen it to change headcounts or points.'
              : 'This session is finished. A games leader can reopen it to change headcounts or points.'
            : undefined
        }
        busy={teamsPending}
        onSaveHeadcount={(teamId, headcount) => saveHeadcount.mutate({ teamId, headcount })}
        onAddAdjustment={(teamId, points, reason) =>
          addAdjustment.mutate({ teamId, points, reason })
        }
        onClearAdjustment={(adjustment) => clearAdjustment.mutate(adjustment)}
      />

      <ConfirmDialog
        open={finishing}
        title="Finish this session?"
        confirmLabel="Finish session"
        busy={finish.isPending}
        onCancel={() => setFinishing(false)}
        onConfirm={() => finish.mutate()}
      >
        <p>
          End tonight's session and mark it as finished. No more rounds can be added, and the scoreboard
          will be finalized. A games leader can reopen it afterwards, for example to add headcounts.
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
