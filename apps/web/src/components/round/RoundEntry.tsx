import { useMutation, useQuery } from '@tanstack/react-query'
import { Handshake, Minus, Plus, RotateCcw, Trash2, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { api, ApiError } from '@/lib/apiClient'
import { useDebounced } from '@/lib/hooks/useDebounced'
import { queryKeys } from '@/lib/queryClient'
import {
  blockingReason,
  emptyDraft,
  fromRound,
  placedTeams,
  roundDraftReducer,
  slotOf,
  startSlots,
  toEntries,
  type DraftAction,
  type DraftState,
} from '@/lib/roundDraft'
import type {
  Game,
  Preview,
  RoundRecorded,
  RoundSummary,
  SessionDetail,
  SessionTeam,
} from '@/lib/types'

const BONUS_STEP = 10
const PREVIEW_DEBOUNCE_MS = 250
const ORDINALS = ['1st', '2nd', '3rd', '4th', '5th', '6th']

function ordinal(place: number): string {
  return ORDINALS[place - 1] ?? `${place}th`
}

export function RoundEntry({
  session,
  editing,
  editingLabel,
  shortcuts = true,
  onDone,
  onCancel,
}: {
  session: SessionDetail
  /**
   * A recorded round being corrected, rather than a new one being entered.
   *
   * Editing saves over the round in place, so it keeps its id, its number and
   * its position in the night. Nothing happens until save, which is what makes
   * backing out of a correction free.
   */
  editing?: RoundSummary
  /** How the round being corrected is numbered on screen. */
  editingLabel?: string
  /** Off for a form sitting behind a modal, so both do not answer one keypress. */
  shortcuts?: boolean
  onDone: () => void
  onCancel?: () => void
}) {
  const [draft, dispatch] = useReducer(roundDraftReducer, undefined, () => emptyDraft())
  const [showShortcuts, setShowShortcuts] = useState(false)
  const [confirming, setConfirming] = useState(false)

  const games = useQuery<Game[]>({
    queryKey: queryKeys.games(session.divisionId),
    queryFn: ({ signal }) => api.games(session.divisionId, signal),
    staleTime: Infinity,
  })

  const initialDraft = useMemo(() => (editing ? fromRound(editing) : null), [editing])

  const teamIds = useMemo(() => session.teams.map((team) => team.teamId), [session.teams])
  const blocked = blockingReason(draft, teamIds)

  // While a tie is being collected, everything except the team blocks is out of
  // reach. Undoing or resetting halfway through building a shared place leaves
  // the draft somewhere nobody meant to put it.
  const locked = draft.tieArmed

  /* -------------------------------------------------- draft persistence */

  // A correction is not persisted: it starts from what is recorded, and
  // abandoning it has to leave both the round and the next round's draft alone.
  const storageKey = editing ? null : `awana.draft.${session.id}`
  const restored = useRef(false)

  useEffect(() => {
    if (restored.current) return
    restored.current = true

    if (initialDraft) {
      dispatch({ type: 'restore', state: initialDraft })
      return
    }

    try {
      const saved = storageKey && sessionStorage.getItem(storageKey)
      if (saved) dispatch({ type: 'restore', state: JSON.parse(saved) as DraftState })
    } catch {
      // A corrupt or unreadable draft is not worth failing over. Start fresh.
    }
  }, [storageKey, initialDraft])

  useEffect(() => {
    if (!storageKey) return

    try {
      if (draft.groups.length === 0) sessionStorage.removeItem(storageKey)
      else sessionStorage.setItem(storageKey, JSON.stringify(draft))
    } catch {
      // Losing the ability to restore is survivable; failing the round is not.
    }
  }, [draft, storageKey])

  /* ------------------------------------------------------- live preview */

  const entries = useMemo(() => toEntries(draft), [draft])
  const previewInput = useMemo(
    () => ({ gameId: draft.gameId, multiplier: draft.multiplier, entries }),
    [draft.gameId, draft.multiplier, entries],
  )
  const settled = useDebounced(previewInput, PREVIEW_DEBOUNCE_MS)

  const preview = useQuery<Preview>({
    queryKey: ['preview', session.id, settled],
    queryFn: ({ signal }) =>
      api.preview(
        session.id,
        { gameId: settled.gameId!, multiplier: settled.multiplier, entries: settled.entries },
        signal,
      ),
    enabled: Boolean(settled.gameId) && settled.entries.length > 0,
    // The server is the only authority on points. Recomputing them here would
    // be a second implementation that eventually disagrees with what is stored.
    staleTime: Infinity,
  })

  /* ------------------------------------------------------------ confirm */

  // One id per round ATTEMPT, held across retries so a lost response cannot
  // score the round twice, and replaced only once a round is safely recorded.
  const requestId = useRef(crypto.randomUUID())

  const record = useMutation<RoundRecorded>({
    mutationFn: () =>
      editing
        ? api.updateRound(editing.id, {
            gameId: draft.gameId!,
            multiplier: draft.multiplier,
            entries,
          })
        : api.recordRound(session.id, {
            clientRequestId: requestId.current,
            gameId: draft.gameId!,
            multiplier: draft.multiplier,
            entries,
          }),
    onSuccess: () => {
      requestId.current = crypto.randomUUID()
      setConfirming(false)

      if (!editing) {
        dispatch({ type: 'reset' })
        try {
          if (storageKey) sessionStorage.removeItem(storageKey)
        } catch {
          // Nothing to do.
        }
      }

      onDone()
    },
  })

  const ready = !blocked && !record.isPending

  const submit = useCallback(() => {
    if (!ready) return

    // Opening the editor was already the deliberate act for a correction, and
    // a second dialog on top of the first one only reads as an obstacle. A new
    // round has no such gate, so it gets the confirmation step.
    if (editing) record.mutate()
    else setConfirming(true)
  }, [ready, editing, record])

  /* ---------------------------------------------------------- shortcuts */

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      if (target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) return
      if (!shortcuts) return

      // The dialog owns the keyboard while it is up. Enter belongs to its
      // confirm button, not to a second attempt at opening it.
      if (confirming) return

      const index = Number(event.key) - 1
      if (index >= 0 && index < session.teams.length) {
        dispatch({ type: 'tapTeam', teamId: session.teams[index].teamId })
        return
      }

      // Only placing teams and ending the tie are reachable while collecting
      // one, matching exactly what the buttons allow.
      if (locked && event.key.toLowerCase() !== 't') return

      switch (event.key.toLowerCase()) {
        case 't':
          dispatch({ type: 'toggleTieArm' })
          break
        case 'd': {
          const placed = placedTeams(draft)
          if (placed.length > 0) dispatch({ type: 'toggleDq', teamId: placed[placed.length - 1] })
          break
        }
        case 'z':
          if (event.ctrlKey || event.metaKey) {
            event.preventDefault()
            dispatch({ type: 'undo' })
          }
          break
        case 'enter':
          event.preventDefault()
          submit()
          break
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [draft, session.teams, submit, locked, confirming, shortcuts])

  /* -------------------------------------------------------------- render */

  const byId = useMemo(
    () => new Map(session.teams.map((team) => [team.teamId, team])),
    [session.teams],
  )
  const pointsFor = new Map(preview.data?.awards.map((a) => [a.teamId, a.points]) ?? [])
  const slots = startSlots(draft)

  // Why the points cannot be shown, when that is the case.
  const previewProblem =
    preview.error instanceof ApiError
      ? `Points unavailable: ${preview.error.message}`
      : preview.error
        ? 'Points unavailable. Check the connection.'
        : preview.data?.isValid === false
          ? preview.data.errors.map((e) => e.message).join(' ')
          : null

  const gameName = games.data?.find((game) => game.id === draft.gameId)?.name ?? 'this game'

  return (
    <section className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_400px] lg:items-start">
      {editingLabel && (
        <h2 className="text-lg font-bold tracking-tight lg:col-span-2">
          Editing {editingLabel}
        </h2>
      )}

      <div className="mx-auto flex w-full max-w-md flex-col gap-5">
        <GamePicker
          games={games.data ?? []}
          gameId={draft.gameId}
          multiplier={draft.multiplier}
          disabled={locked}
          onGame={(gameId) => dispatch({ type: 'setGame', gameId })}
          onMultiplier={(multiplier) => dispatch({ type: 'setMultiplier', multiplier })}
        />

        <div>
          <span className="mb-2.5 block text-xs font-semibold tracking-wide text-muted-foreground uppercase">
            Tap in finish order
          </span>

          {draft.gameId ? (
            <div className="grid grid-cols-2 gap-2.5">
              {session.teams.map((team) => (
                <TeamBlock
                  key={team.teamId}
                  team={team}
                  place={slotOf(draft, team.teamId)}
                  arming={locked}
                  onTap={() => dispatch({ type: 'tapTeam', teamId: team.teamId })}
                />
              ))}
            </div>
          ) : (
            <p className="rounded-xl border border-dashed p-6 text-center text-sm text-muted-foreground">
              Choose a game above to start recording this round.
            </p>
          )}

          {/* Directly under the blocks they act on, rather than off in a header
              row, because these three are used mid tap and not before. */}
          <div className="mt-2.5 grid grid-cols-3 gap-2">
            <Button
              variant={locked ? 'default' : 'outline'}
              aria-pressed={locked}
              disabled={!draft.gameId}
              onClick={() => dispatch({ type: 'toggleTieArm' })}
            >
              <Handshake />
              Tie
            </Button>

            <Button
              variant="outline"
              disabled={locked || draft.past.length === 0}
              onClick={() => dispatch({ type: 'undo' })}
            >
              <RotateCcw />
              Undo
            </Button>

            <Button
              variant="destructive"
              disabled={locked || draft.groups.length === 0}
              onClick={() => dispatch({ type: 'reset' })}
            >
              <Trash2 />
              Reset
            </Button>
          </div>
        </div>
      </div>

      {/* Finish order and its points, next to the button that sends them. They
          were two panels asking the same question, and the row already carries
          what each team would score. */}
      <div className="mx-auto flex w-full max-w-md flex-col gap-3 lg:sticky lg:top-20 lg:max-w-none">
        <div className="rounded-xl border p-4">
          <div className="mb-3 flex items-baseline justify-between">
            <span className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
              Finish order
            </span>
            {preview.isFetching && (
              <span className="text-xs text-muted-foreground">Checking...</span>
            )}
          </div>

          {draft.groups.length === 0 ? (
            <p className="py-2 text-center text-sm text-muted-foreground">
              {draft.gameId ? 'Tap a team to start.' : 'Choose a game to start.'}
            </p>
          ) : (
            <ul className="flex flex-col gap-2.5">
              {draft.groups.map((group, index) => (
                <li key={group.join('-')}>
                  <div className="mb-1.5 flex items-center gap-2">
                    <span className="text-sm font-bold">{ordinal(slots[index])}</span>
                    {group.length > 1 && (
                      <span className="text-xs text-muted-foreground">
                        {group.length} teams tied
                      </span>
                    )}
                  </div>

                  <div className="flex flex-col gap-2">
                    {group.map((teamId) => (
                      <TeamLine
                        key={teamId}
                        team={byId.get(teamId)}
                        teamId={teamId}
                        dq={draft.dq.includes(teamId)}
                        bonus={draft.bonus[teamId] ?? 0}
                        points={pointsFor.get(teamId)}
                        disabled={locked}
                        dispatch={dispatch}
                      />
                    ))}
                  </div>
                </li>
              ))}
            </ul>
          )}

          {previewProblem && (
            <p className="mt-3 border-t pt-3 text-xs leading-relaxed text-destructive">
              {previewProblem}
            </p>
          )}
        </div>

        {record.error instanceof ApiError && (
          <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-3 text-sm">
            {record.error.message}
          </p>
        )}

        <Button
          size="lg"
          onClick={submit}
          disabled={!ready}
          className={[
            'h-14 text-base',
            // Ready to send looks different from waiting on something, from
            // across the room and out of the corner of an eye.
            ready ? 'text-lg font-bold shadow-lg shadow-primary/25 ring-2 ring-primary/25' : '',
          ].join(' ')}
        >
          {record.isPending
            ? 'Saving...'
            : (blocked ?? (editing ? 'Save changes' : 'Confirm round'))}
        </Button>

        {onCancel && (
          <Button variant="ghost" size="lg" disabled={record.isPending} onClick={onCancel}>
            Cancel
          </Button>
        )}

        <button
          type="button"
          onClick={() => setShowShortcuts((open) => !open)}
          className="mt-1 hidden text-left text-xs text-muted-foreground underline-offset-4 hover:underline lg:block"
        >
          {showShortcuts ? 'Hide' : 'Show'} keyboard shortcuts
        </button>

        {showShortcuts && (
          <dl className="hidden gap-x-4 gap-y-1.5 text-xs text-muted-foreground lg:grid lg:grid-cols-[auto_1fr]">
            <Shortcut keys="1 – 4" what="Place the next team" />
            <Shortcut keys="T" what="Start or end a tie" />
            <Shortcut keys="D" what="Disqualify the last placed" />
            <Shortcut keys="Ctrl Z" what="Undo" />
            <Shortcut keys="Enter" what="Confirm round" />
          </dl>
        )}
      </div>

      <ConfirmDialog
        open={confirming}
        title="Confirm this round?"
        confirmLabel="Confirm round"
        cancelLabel="Keep editing"
        busy={record.isPending}
        onCancel={() => setConfirming(false)}
        onConfirm={() => record.mutate()}
      >
        <p>
          {gameName}
          {draft.multiplier !== 1 && `, worth ×${draft.multiplier}`}. This goes on the board right
          away.
        </p>

        <ul className="mt-3 flex flex-col gap-1.5">
          {draft.groups.flatMap((group, index) =>
            group.map((teamId) => (
              <li key={teamId} className="flex items-center gap-2.5">
                <span className="w-8 text-xs font-semibold tabular-nums">
                  {ordinal(slots[index])}
                </span>
                <span
                  className="inline-flex h-6 min-w-16 items-center justify-center rounded-md px-2 text-xs font-bold"
                  style={{
                    background: byId.get(teamId)?.colorHex,
                    color: byId.get(teamId)?.textOnColorHex,
                  }}
                >
                  {byId.get(teamId)?.name}
                </span>
                {draft.dq.includes(teamId) && (
                  <span className="text-xs font-bold text-destructive">DQ</span>
                )}
                <span className="ml-auto text-sm font-bold tabular-nums text-foreground">
                  {pointsFor.has(teamId) ? Math.round(pointsFor.get(teamId)!) : '...'}
                </span>
              </li>
            )),
          )}
        </ul>
      </ConfirmDialog>
    </section>
  )
}

/* ------------------------------------------------------------ sub views */

function Shortcut({ keys, what }: { keys: string; what: string }) {
  return (
    <>
      <dt className="font-mono font-semibold text-foreground">{keys}</dt>
      <dd>{what}</dd>
    </>
  )
}

function GamePicker({
  games,
  gameId,
  multiplier,
  disabled,
  onGame,
  onMultiplier,
}: {
  games: Game[]
  gameId: string | null
  multiplier: number
  disabled: boolean
  onGame: (id: string) => void
  onMultiplier: (n: number) => void
}) {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <label className="flex min-w-0 flex-1 flex-col gap-1.5">
        <span className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
          Game
        </span>
        <select
          value={gameId ?? ''}
          disabled={disabled}
          onChange={(event) => onGame(event.target.value)}
          className="h-11 w-full min-w-0 rounded-lg border border-border bg-background px-3 text-sm font-medium disabled:opacity-50"
        >
          {/* Nothing preselected. Picking the game is the first deliberate act
              of the round, and a default is the kind of thing that goes unnoticed
              and then has to be corrected after the fact. */}
          <option value="" disabled>
            Select a game...
          </option>
          {games.map((game) => (
            <option key={game.id} value={game.id}>
              {game.name}
            </option>
          ))}
        </select>
      </label>

      <div className="flex flex-col gap-1.5">
        <span className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
          Worth
        </span>
        {/* A final round or a tug-of-war is often worth double. It multiplies
            placement points only, never a bonus. */}
        <div
          className="flex h-11 gap-0.5 rounded-lg border border-border p-0.5"
          role="group"
          aria-label="Round multiplier"
        >
          {[1, 2].map((value) => (
            <button
              key={value}
              type="button"
              disabled={disabled}
              onClick={() => onMultiplier(value)}
              aria-pressed={multiplier === value}
              className={[
                'min-w-12 rounded-md px-2 text-sm font-semibold transition-colors disabled:opacity-50',
                multiplier === value
                  ? 'bg-primary text-primary-foreground'
                  : 'text-muted-foreground hover:text-foreground',
              ].join(' ')}
            >
              ×{value}
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}

function TeamBlock({
  team,
  place,
  arming,
  onTap,
}: {
  team: SessionTeam
  /** The finishing place, which tied teams SHARE. Null until tapped. */
  place: number | null
  arming: boolean
  onTap: () => void
}) {
  const placed = place !== null

  return (
    <button
      type="button"
      onClick={onTap}
      disabled={placed}
      data-team-block
      className="relative flex aspect-square w-full items-center justify-center rounded-2xl text-xl font-bold transition-colors"
      style={{
        // A placed block lightens toward white while keeping its own hue, so
        // the teams still to come stay solid and obvious beside it. Not
        // grayscale, which turns yellow to tan, and not opacity, which takes
        // yellow below readable contrast.
        background: placed ? `color-mix(in oklch, ${team.colorHex}, white 62%)` : team.colorHex,
        color: placed ? 'oklch(0.145 0 0)' : team.textOnColorHex,
        // Marks which blocks a tie can still take. Drawn INSIDE the block, so
        // it does not disappear against whichever page background happens to be
        // behind it, which is how a black outline went missing in the dark
        // theme. White on every team rather than each team's own text color, so
        // the four read as one signal instead of four different ones.
        boxShadow:
          arming && !placed ? `inset 0 0 0 5px ${team.colorHex}, inset 0 0 0 10px white` : undefined,
      }}
    >
      {team.name}

      {placed && (
        <span
          className="pointer-events-none absolute top-2.5 right-2.5 flex size-7 items-center justify-center rounded-full text-sm font-bold"
          style={{ background: 'rgb(0 0 0 / 12%)' }}
        >
          {place}
        </span>
      )}
    </button>
  )
}

/** One team inside a finishing place: what happened to it, and what it scored. */
function TeamLine({
  team,
  teamId,
  dq,
  bonus,
  points,
  disabled,
  dispatch,
}: {
  team: SessionTeam | undefined
  teamId: string
  dq: boolean
  bonus: number
  points: number | undefined
  disabled: boolean
  dispatch: React.Dispatch<DraftAction>
}) {
  return (
    <div className="flex items-center gap-1.5">
      {/* The controls wrap among themselves on a narrow phone. The points stay
          pinned to the first line, where the eye is already looking. */}
      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-1.5">
        <span
          className="inline-flex h-8 min-w-18 items-center justify-center rounded-lg px-2.5 text-sm font-bold"
          style={{
            background: team?.colorHex,
            color: team?.textOnColorHex,
            textDecoration: dq ? 'line-through' : undefined,
            textDecorationThickness: dq ? '2px' : undefined,
          }}
        >
          {team?.name ?? 'Unknown'}
        </span>

        {/* Covers a rule break and a team that never finished alike: the slot
            is kept, nothing is scored, and nobody behind moves up. */}
        <Toggle on={dq} disabled={disabled} onClick={() => dispatch({ type: 'toggleDq', teamId })}>
          DQ
        </Toggle>

        <BonusControl
          value={bonus}
          disabled={disabled}
          onChange={(points) => dispatch({ type: 'setBonus', teamId, points })}
        />

        <button
          type="button"
          disabled={disabled}
          aria-label="Remove from the round"
          title="Remove from the round"
          onClick={() => dispatch({ type: 'remove', teamId })}
          className="flex size-8 items-center justify-center rounded-lg border text-muted-foreground hover:bg-muted disabled:opacity-40"
        >
          <X className="size-3.5" />
        </button>
      </div>

      <span className="shrink-0 text-base font-bold tabular-nums">
        {points === undefined ? '' : Math.round(points)}
      </span>
    </div>
  )
}

/**
 * Bonus points.
 *
 * Steps of ten, because that is what the bonus bucket has historically been
 * worth, but the value is typeable: the archive shows it varied, and a future
 * game will award something else again.
 */
function BonusControl({
  value,
  disabled,
  onChange,
}: {
  value: number
  disabled: boolean
  onChange: (points: number) => void
}) {
  if (value === 0) {
    return (
      <Toggle on={false} disabled={disabled} onClick={() => onChange(BONUS_STEP)}>
        <Plus className="size-3.5" />
        Bonus
      </Toggle>
    )
  }

  return (
    <span className="inline-flex h-8 items-center rounded-lg border">
      <button
        type="button"
        disabled={disabled}
        aria-label="Less bonus"
        onClick={() => onChange(Math.max(0, value - BONUS_STEP))}
        className="flex size-7 items-center justify-center rounded-l-lg hover:bg-muted disabled:opacity-40"
      >
        <Minus className="size-3.5" />
      </button>

      <input
        type="number"
        inputMode="numeric"
        value={value}
        disabled={disabled}
        aria-label="Bonus points"
        onChange={(event) => onChange(Number(event.target.value))}
        className="h-full w-12 border-x bg-transparent text-center text-sm font-bold tabular-nums outline-none disabled:opacity-40"
      />

      <button
        type="button"
        disabled={disabled}
        aria-label="More bonus"
        onClick={() => onChange(value + BONUS_STEP)}
        className="flex size-7 items-center justify-center rounded-r-lg hover:bg-muted disabled:opacity-40"
      >
        <Plus className="size-3.5" />
      </button>
    </span>
  )
}

function Toggle({
  children,
  on,
  disabled,
  onClick,
}: {
  children: React.ReactNode
  on: boolean
  disabled: boolean
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={on}
      className={[
        'inline-flex h-8 items-center gap-1 rounded-lg border px-2.5 text-xs font-medium transition-colors disabled:opacity-40',
        on
          ? 'border-destructive bg-destructive/10 font-bold text-destructive'
          : 'border-border hover:bg-muted',
      ].join(' ')}
    >
      {children}
    </button>
  )
}
