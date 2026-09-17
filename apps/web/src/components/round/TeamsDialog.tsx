import { Minus, Plus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { useDebounced } from '@/lib/hooks/useDebounced'
import type { Adjustment, SessionTeam } from '@/lib/types'
import { formatPoints } from '@/lib/format'

/**
 * The per-team things that are not a round.
 *
 * Both live behind one button because both are occasional: a headcount is
 * taken once a night, an adjustment only when a leader decides something. On
 * the page they were permanent furniture around the one screen that is used
 * every few minutes.
 */
export function TeamsDialog({
  open,
  onClose,
  teams,
  adjustments,
  canCount,
  canAdjust,
  note,
  busy,
  onSaveHeadcount,
  onAddAdjustment,
  onClearAdjustment,
}: {
  open: boolean
  onClose: () => void
  teams: SessionTeam[]
  adjustments: Adjustment[]
  /** Heads are counted before the games start, so this outlives Setup. */
  canCount: boolean
  /** Points are decided while the games run, and not after they are over. */
  canAdjust: boolean
  /**
   * Why nothing here can be changed, when that is the case. Greyed out controls
   * with no reason read as broken, and the reason is usually one button away.
   */
  note?: string
  busy: boolean
  onSaveHeadcount: (teamId: string, headcount: number | null) => void
  onAddAdjustment: (teamId: string, points: number, reason: string) => void
  onClearAdjustment: (adjustment: Adjustment) => void
}) {
  return (
    <Modal open={open} onClose={onClose} className="w-[min(30rem,calc(100%-2rem))]">
      <div className="flex flex-col gap-5 p-5">
        <h2 className="text-lg font-bold tracking-tight">Teams</h2>

        {note && <p className="rounded-lg bg-muted p-3 text-sm text-muted-foreground">{note}</p>}

        <Headcounts teams={teams} editable={canCount} onSave={onSaveHeadcount} />

        <Points
          teams={teams}
          adjustments={adjustments}
          editable={canAdjust}
          busy={busy}
          onAdd={onAddAdjustment}
          onClear={onClearAdjustment}
        />

        <Button variant="outline" size="lg" onClick={onClose}>
          Done
        </Button>
      </div>
    </Modal>
  )
}

function Label({ children }: { children: React.ReactNode }) {
  return (
    <h3 className="mb-2.5 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
      {children}
    </h3>
  )
}

/** One width for every team chip, so both lists line up down the dialog. */
const CHIP = 'inline-flex h-9 w-24 shrink-0 items-center justify-center rounded-lg text-sm font-bold'

function TeamChip({ team, name }: { team?: SessionTeam; name?: string }) {
  return (
    <span
      className={CHIP}
      style={{ background: team?.colorHex, color: team?.textOnColorHex }}
    >
      {name ?? team?.name}
    </span>
  )
}

/**
 * How many turned up, per team.
 *
 * A stepper rather than a text box: these are small numbers counted off a room
 * of children, usually by somebody holding a phone in one hand, and tapping is
 * both faster and harder to get wrong than typing.
 *
 * Optional, and it affects no score. This is a scoreboard rather than an
 * attendance system: the database never holds a child's name, so a session
 * with none of these recorded is completely valid.
 */
function Headcounts({
  teams,
  editable,
  onSave,
}: {
  teams: SessionTeam[]
  editable: boolean
  onSave: (teamId: string, headcount: number | null) => void
}) {
  // Text rather than numbers, so a box can be typed into and can sit empty
  // mid edit. The buttons write through the same state, so stepping and typing
  // cannot disagree.
  const [draft, setDraft] = useState<Record<string, string>>(() =>
    Object.fromEntries(teams.map((t) => [t.teamId, t.headcount?.toString() ?? ''])),
  )

  // Saved once the tapping stops, the way the points preview waits for the
  // taps to stop. Five presses of the plus button is one request.
  const settled = useDebounced(draft, 600)

  useEffect(() => {
    for (const team of teams) {
      const text = (settled[team.teamId] ?? '').trim()
      // An empty box means nobody counted, which is not the same as counting
      // nobody.
      const next = text === '' ? null : Number(text)
      if (next !== (team.headcount ?? null)) onSave(team.teamId, next)
    }
    // Only when the settled values change. Including teams or onSave would
    // re-save on every refetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settled])

  const step = (teamId: string, by: number) =>
    setDraft((current) => {
      const text = current[teamId] ?? ''

      // Down from zero goes back to not counted rather than sticking there.
      // Otherwise the only way out of a number typed by mistake is to select
      // the box and delete it, which nobody discovers.
      if (text === '' && by < 0) return current
      if (text === '0' && by < 0) return { ...current, [teamId]: '' }

      return { ...current, [teamId]: Math.max(0, Number(text || 0) + by).toString() }
    })

  return (
    <section>
      <Label>Headcount</Label>

      <div className="flex flex-col gap-2">
        {teams.map((team) => (
          <div key={team.teamId} className="flex items-center gap-3">
            <TeamChip team={team} />

            <span className="inline-flex h-9 items-center overflow-hidden rounded-lg border">
              <button
                type="button"
                disabled={!editable || (draft[team.teamId] ?? '') === ''}
                aria-label={`One fewer in ${team.name}`}
                onClick={() => step(team.teamId, -1)}
                className="flex size-9 items-center justify-center hover:bg-muted disabled:opacity-30"
              >
                <Minus className="size-4" />
              </button>

              <input
                type="text"
                inputMode="numeric"
                disabled={!editable}
                value={draft[team.teamId] ?? ''}
                placeholder="–"
                aria-label={`How many in ${team.name}`}
                onChange={(event) =>
                  setDraft((current) => ({
                    ...current,
                    [team.teamId]: event.target.value.replace(/[^0-9]/g, '').slice(0, 4),
                  }))
                }
                className="h-full w-14 border-x bg-transparent text-center text-sm font-bold tabular-nums outline-none placeholder:font-normal placeholder:text-muted-foreground disabled:opacity-50"
              />

              <button
                type="button"
                disabled={!editable}
                aria-label={`One more in ${team.name}`}
                onClick={() => step(team.teamId, 1)}
                className="flex size-9 items-center justify-center hover:bg-muted disabled:opacity-30"
              >
                <Plus className="size-4" />
              </button>
            </span>
          </div>
        ))}
      </div>
    </section>
  )
}

/**
 * Points a leader decided on, outside the games.
 *
 * Kept apart from round results on purpose, so a total stays explainable: this
 * much was won, this much was decided. Every one carries a written reason,
 * because an unexplained adjustment is indistinguishable from a bug.
 */
function Points({
  teams,
  adjustments,
  editable,
  busy,
  onAdd,
  onClear,
}: {
  teams: SessionTeam[]
  adjustments: Adjustment[]
  editable: boolean
  busy: boolean
  onAdd: (teamId: string, points: number, reason: string) => void
  onClear: (adjustment: Adjustment) => void
}) {
  const [teamId, setTeamId] = useState<string | null>(null)
  const [sign, setSign] = useState<'+' | '-'>('+')
  const [amount, setAmount] = useState('')
  const [reason, setReason] = useState('')

  // The menu has to be portalled into the dialog. A dialog opened with
  // showModal sits in the browser's top layer, so a menu sent to document.body
  // renders underneath it: correctly sized, and invisible.
  const [trigger, setTrigger] = useState<HTMLButtonElement | null>(null)

  const live = adjustments.filter((a) => !a.isVoided)
  const points = (sign === '-' ? -1 : 1) * Number(amount)
  const ready = teamId !== null && amount !== '' && points !== 0 && reason.trim() !== ''

  const submit = () => {
    if (!ready || busy) return
    onAdd(teamId, points, reason.trim())
    setTeamId(null)
    setSign('+')
    setAmount('')
    setReason('')
  }

  const byId = new Map(teams.map((team) => [team.teamId, team]))

  return (
    <section>
      <Label>Adjustments</Label>

      {/* The rule under the list separates it from the add row, so it only
          belongs when there is an add row under it. */}
      {live.length > 0 ? (
        <ul
          className={[
            'flex flex-col gap-2',
            editable ? 'mb-3 border-b pb-3' : '',
          ].join(' ')}
        >
          {live.map((adjustment) => {
            const team = byId.get(adjustment.teamId)
            return (
              <li key={adjustment.id} className="flex items-center gap-3">
                <TeamChip team={team} name={adjustment.teamName} />

                <span className="min-w-0 flex-1 truncate text-sm text-muted-foreground">
                  {adjustment.reason}
                </span>

                <span
                  className={[
                    'shrink-0 text-sm font-bold tabular-nums',
                    adjustment.points < 0 ? 'text-destructive' : '',
                  ].join(' ')}
                >
                  {adjustment.points > 0 ? '+' : ''}
                  {formatPoints(adjustment.points)}
                </span>

                {editable && (
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    disabled={busy}
                    aria-label={`Clear the adjustment for ${adjustment.teamName}`}
                    onClick={() => onClear(adjustment)}
                  >
                    <Trash2 />
                  </Button>
                )}
              </li>
            )
          })}
        </ul>
      ) : (
        !editable && <p className="text-sm text-muted-foreground">None this session.</p>
      )}

      {/* The add row wraps on a phone, where four controls on one line squeeze
          the reason down to a box too small to read what is in it. */}
      {editable && (
        <div className="flex flex-wrap items-center gap-2">
          <Select value={teamId ?? undefined} onValueChange={setTeamId}>
            <SelectTrigger
              ref={setTrigger}
              className="h-9 w-24 shrink-0 data-[size=default]:h-9"
              aria-label="Team"
            >
              <SelectValue placeholder="Team" />
            </SelectTrigger>
            <SelectContent container={trigger?.closest('dialog')}>
              {teams.map((team) => (
                <SelectItem key={team.teamId} value={team.teamId}>
                  {team.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          {/* The sign is a choice, not a character to remember to type. A
              minus lost in a number box is a penalty that reads as an award,
              and the box cannot hold a stray one. */}
          <ToggleGroup
            type="single"
            value={sign}
            aria-label="Award or penalty"
            className="h-9 shrink-0 rounded-lg border p-0.5"
            onValueChange={(next) => next && setSign(next as '+' | '-')}
          >
            <ToggleGroupItem
              value="+"
              aria-label="Award points"
              className="w-9 rounded-md data-[state=on]:bg-primary data-[state=on]:text-primary-foreground"
            >
              <Plus className="size-4" />
            </ToggleGroupItem>
            <ToggleGroupItem
              value="-"
              aria-label="Take points away"
              className="w-9 rounded-md data-[state=on]:bg-destructive data-[state=on]:text-destructive-foreground"
            >
              <Minus className="size-4" />
            </ToggleGroupItem>
          </ToggleGroup>

          <input
            type="text"
            inputMode="numeric"
            value={amount}
            placeholder="10"
            aria-label="How many points"
            onChange={(event) => setAmount(event.target.value.replace(/[^0-9]/g, '').slice(0, 5))}
            className="h-9 w-14 shrink-0 rounded-lg border border-border bg-background px-2 text-center text-sm font-bold tabular-nums"
          />

          {/* Its own line, and a textarea: a reason is a sentence somebody
              will read months later trying to explain a total, not a label. */}
          <Textarea
            value={reason}
            maxLength={500}
            rows={2}
            placeholder="Reason"
            aria-label="Reason"
            onChange={(event) => setReason(event.target.value)}
            onKeyDown={(event) => {
              // Enter sends it, since two lines is already more room than most
              // of these need. Shift with it still starts a new line.
              if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault()
                submit()
              }
            }}
            className="basis-full resize-none"
          />

          <Button className="w-full" disabled={!ready || busy} onClick={submit}>
            Add
          </Button>
        </div>
      )}
    </section>
  )
}
