import { useMutation, useQuery } from '@tanstack/react-query'
import { Handshake, Minus, Plus, RotateCcw, Trash2, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
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

/** Taken by an action, so a team never gets one of these as its key. */
const RESERVED_KEYS = new Set(['t', 'z'])

/**
 * A letter per team, from its name.
 *
 * R places Red wherever Red finished. Numbering the teams instead meant 3 was
 * Yellow no matter what, which stops reading as anything once Yellow is in
 * first. A letter that is ambiguous or already an action is left unbound
 * rather than guessed at.
 */
function teamKeyMap(teams: SessionTeam[]): Map<string, string> {
  const counts = new Map<string, number>()

  for (const team of teams) {
    const letter = team.name.trim()[0]?.toLowerCase()
    if (letter) counts.set(letter, (counts.get(letter) ?? 0) + 1)
  }

  const keys = new Map<string, string>()

  for (const team of teams) {
    const letter = team.name.trim()[0]?.toLowerCase()
    if (!letter || RESERVED_KEYS.has(letter) || counts.get(letter) !== 1) continue
    keys.set(letter, team.teamId)
  }

  return keys
}

function ordinal(place: number): string {
  return ORDINALS[place - 1] ?? `${place}th`
}

export function RoundEntry({
  session,
  editing,
  editingLabel,
  shortcuts = true,
  onTieArmed,
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
  /** Lets the page step back too while a tie is being collected. */
  onTieArmed?: (armed: boolean) => void
  onDone: () => void
  onCancel?: () => void
}) {
  const [draft, dispatch] = useReducer(roundDraftReducer, undefined, () => emptyDraft())
  const [showShortcuts, setShowShortcuts] = useState(false)
  const [confirming, setConfirming] = useState(false)

  const games = useQuery<Game[]>({
    queryKey: queryKeys.games(),
    queryFn: ({ signal }) => api.games(signal),
    staleTime: Infinity,
  })

  const initialDraft = useMemo(() => (editing ? fromRound(editing) : null), [editing])

  const teamIds = useMemo(() => session.teams.map((team) => team.teamId), [session.teams])
  const teamKeys = useMemo(() => teamKeyMap(session.teams), [session.teams])
  const blocked = blockingReason(draft, teamIds)

  // While a tie is being collected, everything except the team blocks is out of
  // reach. Undoing or resetting halfway through building a shared place leaves
  // the draft somewhere nobody meant to put it.
  const locked = draft.tieArmed

  // And everything else steps back while it is, so the blocks are the only lit
  // thing on screen and finishing the tie is the obvious next move. Said by
  // showing rather than by a line of text telling the scorekeeper to read.
  const recede = ['transition-opacity duration-200', locked ? 'opacity-40' : ''].join(' ')

  // With everyone placed a tie has nothing left to collect. It stays available
  // while one is being collected, since that button is also how it ends.
  const canTie = Boolean(draft.gameId) && (locked || placedTeams(draft).length < teamIds.length)

  useEffect(() => {
    onTieArmed?.(locked)
    // Leaving mid tie must not strand the page dimmed.
    return () => onTieArmed?.(false)
  }, [locked, onTieArmed])

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

  // Before a game is chosen the button says what it is for rather than what is
  // missing. The empty finish order panel is already asking for the game, and
  // a button whose only label is an instruction reads as part of the form.
  const submitLabel = editing ? 'Save changes' : 'Confirm round'

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

      // Disqualifying is deliberately absent. It is the one entry that
      // accuses a team of something, and a stray keypress is no way to make
      // that call, so it stays on the row's own button.

      // The dialog owns the keyboard while it is up. Enter belongs to its
      // confirm button, not to a second attempt at opening it.
      if (confirming) return

      const key = event.key.toLowerCase()

      // Before the tie guard: placing teams is the one thing a tie still needs.
      const teamId = teamKeys.get(key)
      if (teamId) {
        dispatch({ type: 'tapTeam', teamId })
        return
      }

      // Only placing teams and ending the tie are reachable while collecting
      // one, matching exactly what the buttons allow.
      if (locked && key !== 't') return

      switch (key) {
        case 't':
          // Same guard the button carries. Without it the key could arm a tie
          // with no game chosen, which locks the other controls and leaves the
          // disabled Tie button as the only way out.
          if (canTie) dispatch({ type: 'toggleTieArm' })
          break
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
  }, [draft, teamKeys, submit, locked, canTie, confirming, shortcuts])

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
    <section className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_440px] lg:items-start">
      {editingLabel && (
        <h2 className="text-lg font-bold tracking-tight lg:col-span-2">
          Editing {editingLabel}
        </h2>
      )}

      <div className="mx-auto flex w-full max-w-md flex-col gap-5">
        <div className={recede}>
          <GamePicker
            games={games.data ?? []}
            gameId={draft.gameId}
            multiplier={draft.multiplier}
            disabled={locked}
            onGame={(gameId) => dispatch({ type: 'setGame', gameId })}
            onMultiplier={(multiplier) => dispatch({ type: 'setMultiplier', multiplier })}
          />
        </div>

        <div>
          <span
            className={[
              'mb-2.5 block text-xs font-semibold tracking-wide uppercase',
              locked ? 'text-foreground' : 'text-muted-foreground',
            ].join(' ')}
          >
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
              disabled={!canTie}
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
      <div
        className={`mx-auto flex w-full max-w-md flex-col gap-3 lg:sticky lg:top-20 lg:max-w-none ${recede}`}
      >
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
            <ul>
              {draft.groups.map((group, index) => (
                <li
                  key={group.join('-')}
                  className="flex gap-2 border-t border-border/70 py-3 first:border-t-0 first:pt-0 last:pb-0 sm:gap-3"
                >
                  {/* The place reads down the left edge, one badge per place
                      however many teams share it. Tied teams hang off the same
                      badge under one rule, which says they share a place
                      without a sentence explaining it. */}
                  <div className="flex w-9 shrink-0 flex-col items-center gap-1.5 sm:w-11">
                    <span className="inline-flex h-7 w-full items-center justify-center rounded-lg bg-muted text-xs font-bold tabular-nums">
                      {ordinal(slots[index])}
                    </span>
                    {group.length > 1 && <span className="w-px flex-1 bg-border" />}
                  </div>

                  <div className="flex min-w-0 flex-1 flex-col gap-2">
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
          {record.isPending ? 'Saving...' : !draft.gameId ? submitLabel : (blocked ?? submitLabel)}
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
            {session.teams.map((team) => {
              const key = [...teamKeys].find(([, id]) => id === team.teamId)?.[0]
              return key ? (
                <Shortcut key={team.teamId} keys={key.toUpperCase()} what={`Place ${team.name}`} />
              ) : null
            })}
            <Shortcut keys="T" what="Start or end a tie" />
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
  // Where the menu has to be portalled, asked of the DOM rather than threaded
  // down as context: whatever the trigger is inside is the right answer, and
  // there is no arrangement of providers that can disagree with it.
  const [trigger, setTrigger] = useState<HTMLButtonElement | null>(null)

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="flex min-w-0 flex-1 flex-col gap-1.5">
        <span className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
          Game
        </span>

        {/* Nothing preselected. Picking the game is the first deliberate act of
            the round, and a default is the kind of thing that goes unnoticed and
            then has to be corrected after the fact. */}
        <Select value={gameId ?? undefined} disabled={disabled} onValueChange={onGame}>
          <SelectTrigger
            ref={setTrigger}
            className="h-11 w-full text-sm font-medium data-[size=default]:h-11"
            aria-label="Game"
          >
            <SelectValue placeholder="Select a game..." />
          </SelectTrigger>
          <SelectContent container={trigger?.closest('dialog')}>
            {games.map((game) => (
              <SelectItem key={game.id} value={game.id}>
                {game.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="flex flex-col gap-1.5">
        <span className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
          Multiplier
        </span>
        {/* A final round or a tug-of-war is often worth double. It multiplies
            placement points only, never a bonus. */}
        <ToggleGroup
          type="single"
          value={String(multiplier)}
          disabled={disabled}
          aria-label="Round multiplier"
          className="h-11 rounded-lg border border-border p-0.5"
          onValueChange={(value) => {
            // A group with nothing selected is not a state this has: pressing
            // the active one again should leave the round where it is.
            if (value) onMultiplier(Number(value))
          }}
        >
          {[1, 2].map((value) => (
            <ToggleGroupItem
              key={value}
              value={String(value)}
              size="lg"
              aria-label={`Worth ${value} times placement points`}
              className="min-w-12 rounded-md text-sm font-semibold data-[state=on]:bg-primary data-[state=on]:text-primary-foreground"
            >
              ×{value}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
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
    <div className="flex items-center gap-1 sm:gap-1.5">
      {/* Every part gives up width on a phone so a team still reads as one
          line there. The controls wrap among themselves if it comes to it, and
          the points stay pinned to the first line where the eye already is. */}
      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-1 sm:gap-1.5">
        <span
          className="inline-flex h-8 min-w-14 items-center justify-center rounded-lg px-2 text-sm font-bold sm:min-w-18 sm:px-2.5"
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

      {/* Fixed width so the column lines up down the card rather than
          shuffling as the numbers change. */}
      <span
        className={[
          'w-10 shrink-0 text-right text-base font-bold tabular-nums',
          dq ? 'text-muted-foreground' : '',
        ].join(' ')}
      >
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
 *
 * A text input rather than a number one. A number input scrolls its value when
 * the wheel passes over it and grows stepper arrows of its own next to the
 * ones here, and a scoreboard that quietly changes a score because someone
 * scrolled the page is not worth the free keyboard.
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
      <Toggle
        on={false}
        disabled={disabled}
        label="Add bonus points"
        onClick={() => onChange(BONUS_STEP)}
      >
        <Plus className="size-3.5" />
        <span className="hidden sm:inline">Bonus</span>
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
        className="flex size-6 items-center justify-center rounded-l-lg hover:bg-muted disabled:opacity-40 sm:size-7"
      >
        <Minus className="size-3.5" />
      </button>

      <BonusInput value={value} disabled={disabled} onChange={onChange} />

      <button
        type="button"
        disabled={disabled}
        aria-label="More bonus"
        onClick={() => onChange(value + BONUS_STEP)}
        className="flex size-6 items-center justify-center rounded-r-lg hover:bg-muted disabled:opacity-40 sm:size-7"
      >
        <Plus className="size-3.5" />
      </button>
    </span>
  )
}

function BonusInput({
  value,
  disabled,
  onChange,
}: {
  value: number
  disabled: boolean
  onChange: (points: number) => void
}) {
  // Held locally so the field can sit empty, or on a bare zero, mid edit.
  // Committing either straight through reads as "no bonus" and folds the whole
  // control away between deleting the old number and typing the new one.
  // Nothing is committed until it is a real number, or until focus leaves.
  const [text, setText] = useState(() => String(value))
  const [seen, setSeen] = useState(value)

  // Adjusted during render rather than in an effect, so a step from the
  // buttons shows up in the same pass that changed it.
  if (value !== seen) {
    setSeen(value)
    setText(String(value))
  }

  return (
    <input
      type="text"
      inputMode="numeric"
      value={text}
      disabled={disabled}
      aria-label="Bonus points"
      onChange={(event) => {
        const digits = event.target.value.replace(/[^0-9]/g, '').slice(0, 4)
        setText(digits)
        if (Number(digits) > 0) onChange(Number(digits))
      }}
      onBlur={() => {
        // Leaving it empty or at zero is the deliberate way to take a bonus
        // back off, as is stepping down past ten.
        if (Number(text) === 0) onChange(0)
      }}
      className="h-full w-10 border-x bg-transparent text-center text-sm font-bold tabular-nums outline-none disabled:opacity-40 sm:w-12"
    />
  )
}

function Toggle({
  children,
  on,
  disabled,
  label,
  onClick,
}: {
  children: React.ReactNode
  on: boolean
  disabled: boolean
  /** Needed when the visible label is hidden at narrow widths. */
  label?: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={on}
      aria-label={label}
      title={label}
      className={[
        'inline-flex h-8 items-center gap-1 rounded-lg border px-2 text-xs font-medium transition-colors disabled:opacity-40 sm:px-2.5',
        on
          ? 'border-destructive bg-destructive/10 font-bold text-destructive'
          : 'border-border hover:bg-muted',
      ].join(' ')}
    >
      {children}
    </button>
  )
}
