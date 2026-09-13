import { Pencil, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import type { RoundSummary, SessionTeam } from '@/lib/types'

/**
 * What has been recorded tonight, newest first.
 *
 * During a session the only round anyone asks about is the one that just went
 * up, and making the scorekeeper scroll past an hour of correct rounds to
 * reach it is the wrong default.
 */
export function RoundHistory({
  rounds,
  teams,
  editable,
  busy,
  onClear,
  onEdit,
}: {
  rounds: RoundSummary[]
  /** For the team colors, which a recorded round does not carry itself. */
  teams: SessionTeam[]
  /** Corrections need a running session. */
  editable: boolean
  busy: boolean
  onClear: (round: RoundSummary, reason: string) => void
  onEdit: (round: RoundSummary, label: string) => void
}) {
  const [clearing, setClearing] = useState<{ round: RoundSummary; label: string } | null>(null)

  const colorOf = new Map(teams.map((team) => [team.teamId, team.colorHex]))

  // Cleared rounds are left out entirely: one is a mistake being taken back,
  // and listing it only invites a question about the numbering. It stays in
  // the database either way.
  //
  // Numbered by position rather than by the stored round number. Clearing a
  // round in the middle of the night leaves a hole in the stored numbering,
  // and "round 3, round 5, round 6" invites a question nobody can answer from
  // the board. The database keeps its own numbering, which never moves.
  const live = [...rounds]
    .filter((round) => !round.isVoided)
    .sort((a, b) => a.roundNumber - b.roundNumber)
    .map((round, index) => ({ round, label: `Round ${index + 1}` }))

  return (
    <section>
      <h2 className="mb-3 text-sm font-semibold tracking-wide text-muted-foreground uppercase">
        Rounds
      </h2>

      {live.length === 0 ? (
        <div className="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground">
          No rounds recorded yet.
        </div>
      ) : (
        <ul className="flex flex-col gap-2">
          {[...live].reverse().map(({ round, label }) => (
            <li key={round.id} className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
              <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
                <span className="font-semibold">
                  {label} · {round.gameName}
                </span>

                {round.multiplier !== 1 && (
                  <span className="text-xs font-semibold text-muted-foreground">
                    ×{round.multiplier}
                  </span>
                )}

                {editable && (
                  <div className="ml-auto flex gap-2">
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={busy}
                      onClick={() => onEdit(round, label)}
                    >
                      <Pencil />
                      Edit
                    </Button>
                    <Button
                      size="sm"
                      variant="destructive"
                      disabled={busy}
                      onClick={() => setClearing({ round, label })}
                    >
                      <Trash2 />
                      Clear
                    </Button>
                  </div>
                )}
              </div>

              <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
                {round.teams.map((team) => (
                  <span key={team.teamId} className="inline-flex items-center gap-1.5">
                    <span
                      className="size-2.5 shrink-0 rounded-full"
                      style={{ background: colorOf.get(team.teamId) ?? 'currentColor' }}
                      aria-hidden
                    />
                    {team.teamName}{' '}
                    <span className="font-semibold text-foreground tabular-nums">
                      {Math.round(team.points)}
                    </span>
                    {team.isDisqualified ? ' (DQ)' : ''}
                  </span>
                ))}
              </div>
            </li>
          ))}
        </ul>
      )}

      <ConfirmDialog
        open={clearing !== null}
        title={`Clear ${clearing?.label.toLowerCase()}?`}
        confirmLabel="Clear round"
        destructive
        busy={busy}
        onCancel={() => setClearing(null)}
        onConfirm={() => {
          if (clearing) onClear(clearing.round, 'Cleared by the scorekeeper')
          setClearing(null)
        }}
      >
        <p>
          Its points come off the board straight away and the rounds after it move up a number. To
          fix a wrong place or score instead, use Edit.
        </p>
      </ConfirmDialog>
    </section>
  )
}
