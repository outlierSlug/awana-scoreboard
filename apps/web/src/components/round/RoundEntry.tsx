import { useMutation, useQuery } from '@tanstack/react-query'
import {
  ArrowDown,
  ArrowUp,
  ChevronsUp,
  Minus,
  Plus,
  RotateCcw,
  X,
} from 'lucide-react'
import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { api, ApiError } from '@/lib/apiClient'
import { useDebounced } from '@/lib/hooks/useDebounced'
import { queryKeys } from '@/lib/queryClient'
import {
  blockingReason,
  emptyDraft,
  placedTeams,
  roundDraftReducer,
  toEntries,
  type DraftState,
} from '@/lib/roundDraft'
import type { Game, Preview, RoundRecorded, SessionDetail, SessionTeam } from '@/lib/types'

const BONUS_STEP = 5
const PREVIEW_DEBOUNCE_MS = 250

/**
 * Round entry.
 *
 * The design rule underneath all of this: there are no modes to set BEFORE
 * tapping, except one you can opt into. Tap teams as they cross the line, then
 * correct what needs correcting on the row it belongs to. A tie you already
 * know about can be armed first, because that is how it gets announced.
 */
export function RoundEntry({
  session,
  onRecorded,
}: {
  session: SessionDetail
  onRecorded: () => void
}) {
  const [draft, dispatch] = useReducer(roundDraftReducer, undefined, () => emptyDraft())
  const [showShortcuts, setShowShortcuts] = useState(false)

  const games = useQuery<Game[]>({
    queryKey: queryKeys.games(session.divisionId),
    queryFn: ({ signal }) => api.games(session.divisionId, signal),
    staleTime: Infinity,
  })

  const teamIds = useMemo(() => session.teams.map((team) => team.teamId), [session.teams])
  const blocked = blockingReason(draft, teamIds)

  // Restore a draft the browser threw away. A phone locking in a pocket
  // mid-round must not cost the taps already made.
  const storageKey = `awana.draft.${session.id}`
  const restored = useRef(false)

  useEffect(() => {
    if (restored.current) return
    restored.current = true

    try {
      const saved = sessionStorage.getItem(storageKey)
      if (saved) dispatch({ type: 'restore', state: JSON.parse(saved) as DraftState })
    } catch {
      // A corrupt or unreadable draft is not worth failing over. Start fresh.
    }
  }, [storageKey])

  useEffect(() => {
    try {
      if (draft.groups.length === 0 && draft.absent.length === 0) {
        sessionStorage.removeItem(storageKey)
      } else {
        sessionStorage.setItem(storageKey, JSON.stringify(draft))
      }
    } catch {
      // Losing the ability to restore is survivable; failing the round is not.
    }
  }, [draft, storageKey])

  // Default to the game most likely to be next: the one just played, since a
  // game is usually run several times in a row.
  useEffect(() => {
    if (draft.gameId || !games.data?.length) return

    const lastPlayed = [...session.rounds].reverse().find((round) => !round.isVoided)
    dispatch({ type: 'setGame', gameId: lastPlayed?.gameId ?? games.data[0].id })
  }, [draft.gameId, games.data, session.rounds])

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
    // The server is the only authority on points. Recomputing them in the
    // browser would be a second implementation that eventually disagrees with
    // the number actually stored.
    staleTime: Infinity,
  })

  /* ------------------------------------------------------------ confirm */

  // One id per round ATTEMPT, held across retries so a lost response cannot
  // score the round twice, and replaced only once a round is safely recorded.
  const requestId = useRef(crypto.randomUUID())

  const record = useMutation<RoundRecorded>({
    mutationFn: () =>
      api.recordRound(session.id, {
        clientRequestId: requestId.current,
        gameId: draft.gameId!,
        multiplier: draft.multiplier,
        entries,
      }),
    onSuccess: () => {
      requestId.current = crypto.randomUUID()
      dispatch({ type: 'reset' })
      try {
        sessionStorage.removeItem(storageKey)
      } catch {
        // Nothing to do.
      }
      onRecorded()
    },
  })

  const confirm = useCallback(() => {
    if (blocked || record.isPending) return
    record.mutate()
  }, [blocked, record])

  /* ---------------------------------------------------------- shortcuts */

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      if (target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) return
      if (event.metaKey && event.key.toLowerCase() !== 'z') return

      const index = Number(event.key) - 1
      if (index >= 0 && index < session.teams.length) {
        dispatch({ type: 'tapTeam', teamId: session.teams[index].teamId })
        return
      }

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
          confirm()
          break
        case 'escape':
          dispatch({ type: 'reset' })
          break
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [draft, session.teams, confirm])

  /* -------------------------------------------------------------- render */

  const byId = useMemo(
    () => new Map(session.teams.map((team) => [team.teamId, team])),
    [session.teams],
  )
  const placed = placedTeams(draft)
  const pointsFor = new Map(preview.data?.awards.map((a) => [a.teamId, a.points]) ?? [])

  return (
    <section className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_380px] lg:items-start">
      <div className="flex flex-col gap-5">
        <GameAndMultiplier
          games={games.data ?? []}
          gameId={draft.gameId}
          multiplier={draft.multiplier}
          onGame={(gameId) => dispatch({ type: 'setGame', gameId })}
          onMultiplier={(multiplier) => dispatch({ type: 'setMultiplier', multiplier })}
        />

        <div>
          <div className="mb-2.5 flex items-center gap-2">
            <span
              className={[
                'text-xs font-semibold tracking-wide uppercase',
                draft.tieArmed ? 'text-foreground' : 'text-muted-foreground',
              ].join(' ')}
            >
              {draft.tieArmed ? 'Tap teams that tied' : 'Tap in finish order'}
            </span>

            <div className="ml-auto flex gap-2">
              <Button
                size="sm"
                variant={draft.tieArmed ? 'default' : 'outline'}
                onClick={() => dispatch({ type: 'toggleTieArm' })}
              >
                <ChevronsUp />
                {draft.tieArmed ? 'Done' : 'Tie'}
              </Button>

              <Button
                size="sm"
                variant="outline"
                disabled={draft.past.length === 0}
                onClick={() => dispatch({ type: 'undo' })}
              >
                <RotateCcw />
                Undo
              </Button>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-4 lg:grid-cols-2">
            {session.teams.map((team) => (
              <TeamBlock
                key={team.teamId}
                team={team}
                order={placed.indexOf(team.teamId)}
                absent={draft.absent.includes(team.teamId)}
                arming={draft.tieArmed}
                onTap={() => dispatch({ type: 'tapTeam', teamId: team.teamId })}
                onToggleAbsent={() => dispatch({ type: 'toggleAbsent', teamId: team.teamId })}
              />
            ))}
          </div>
        </div>

        <div>
          <div className="mb-2.5 flex items-center justify-between">
            <span className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
              Finish order
            </span>
            <Button
              size="sm"
              variant="ghost"
              disabled={draft.groups.length === 0}
              onClick={() => dispatch({ type: 'reset' })}
            >
              Clear
            </Button>
          </div>

          {draft.groups.length === 0 ? (
            <p className="rounded-xl border border-dashed p-6 text-center text-sm text-muted-foreground">
              Tap a team as it crosses the line.
            </p>
          ) : (
            <ul className="flex flex-col gap-2">
              {draft.groups.map((group, index) => (
                <DraftRow
                  key={group.join('-')}
                  group={group}
                  index={index}
                  startSlot={draft.groups.slice(0, index).reduce((n, g) => n + g.length, 0) + 1}
                  total={draft.groups.length}
                  teams={byId}
                  dq={draft.dq}
                  bonus={draft.bonus}
                  points={pointsFor}
                  dispatch={dispatch}
                />
              ))}
            </ul>
          )}
        </div>
      </div>

      <div className="flex flex-col gap-3 lg:sticky lg:top-20">
        <PreviewPanel
          teams={session.teams}
          preview={preview.data}
          error={preview.error}
          isPending={preview.isFetching}
          empty={draft.groups.length === 0}
        />

        {record.error instanceof ApiError && (
          <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-3 text-sm">
            {record.error.message}
          </p>
        )}

        <Button size="lg" className="h-14 text-base" disabled={Boolean(blocked) || record.isPending} onClick={confirm}>
          {record.isPending ? 'Sending...' : (blocked ?? 'Confirm round')}
        </Button>

        <p className="text-center text-xs text-muted-foreground">
          Nothing reaches the board until you confirm.
        </p>

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
            <Shortcut keys="T" what="Arm or end a tie" />
            <Shortcut keys="D" what="Disqualify the last placed" />
            <Shortcut keys="Ctrl Z" what="Undo" />
            <Shortcut keys="Enter" what="Confirm round" />
            <Shortcut keys="Esc" what="Clear" />
          </dl>
        )}
      </div>
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

function GameAndMultiplier({
  games,
  gameId,
  multiplier,
  onGame,
  onMultiplier,
}: {
  games: Game[]
  gameId: string | null
  multiplier: number
  onGame: (id: string) => void
  onMultiplier: (n: number) => void
}) {
  return (
    <div className="flex flex-wrap items-center gap-3 rounded-xl bg-muted/60 p-3">
      <label className="flex min-w-0 flex-1 items-center gap-2">
        <span className="sr-only">Game</span>
        <select
          value={gameId ?? ''}
          onChange={(event) => onGame(event.target.value)}
          className="h-10 w-full min-w-0 rounded-lg border border-border bg-background px-3 text-sm font-medium"
        >
          {games.map((game) => (
            <option key={game.id} value={game.id}>
              {game.name}
            </option>
          ))}
        </select>
      </label>

      {/* A final round or a tug-of-war is often worth double. It multiplies
          placement points only, never a bonus. */}
      <div className="flex gap-0.5 rounded-lg bg-background p-0.5" role="group" aria-label="Round multiplier">
        {[1, 2].map((value) => (
          <button
            key={value}
            type="button"
            onClick={() => onMultiplier(value)}
            aria-pressed={multiplier === value}
            className={[
              'h-9 min-w-11 rounded-md text-sm font-semibold transition-colors',
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
  )
}

function TeamBlock({
  team,
  order,
  absent,
  arming,
  onTap,
  onToggleAbsent,
}: {
  team: SessionTeam
  order: number
  absent: boolean
  arming: boolean
  onTap: () => void
  onToggleAbsent: () => void
}) {
  const spent = order >= 0 || absent

  return (
    <div className="relative">
      <button
        type="button"
        onClick={onTap}
        disabled={spent}
        className="flex h-28 w-full items-center justify-center rounded-2xl text-xl font-bold transition-colors disabled:cursor-default"
        style={{
          // A placed block lightens toward white while keeping its own hue, so
          // the teams still to come stay solid and obvious beside it. Not
          // grayscale, which turns yellow to tan, and not opacity, which takes
          // yellow below readable contrast.
          background: spent
            ? `color-mix(in oklch, ${team.colorHex}, white 62%)`
            : team.colorHex,
          color: spent ? 'oklch(0.145 0 0)' : team.textOnColorHex,
          border: arming && !spent ? '3px solid oklch(0.145 0 0)' : '3px solid transparent',
        }}
      >
        {team.name}
      </button>

      {order >= 0 && (
        <span
          className="pointer-events-none absolute top-2 right-2 flex size-6 items-center justify-center rounded-full text-xs font-bold"
          style={{ background: 'rgb(0 0 0 / 12%)', color: 'oklch(0.145 0 0)' }}
        >
          {order + 1}
        </span>
      )}

      {/* Only offered while the team is still available. Once it is in the
          finish order, Remove on its row is the control that makes sense. */}
      {order < 0 && (
        <button
          type="button"
          onClick={onToggleAbsent}
          className="absolute bottom-2 left-1/2 -translate-x-1/2 rounded-md px-2 py-0.5 text-[11px] font-semibold"
          style={{
            background: absent ? 'oklch(0.145 0 0)' : 'rgb(0 0 0 / 10%)',
            color: absent ? 'oklch(0.985 0 0)' : team.textOnColorHex,
          }}
        >
          {absent ? 'Not playing' : 'Sit out'}
        </button>
      )}
    </div>
  )
}

function DraftRow({
  group,
  index,
  startSlot,
  total,
  teams,
  dq,
  bonus,
  points,
  dispatch,
}: {
  group: string[]
  index: number
  /**
   * The first finishing slot this group occupies, which is its real place.
   * NOT its position in the list: two teams sharing first consume slots 1 and
   * 2, so the next row is 3rd. Labelling it 2nd here would disagree with the
   * place the engine actually records.
   */
  startSlot: number
  total: number
  teams: Map<string, SessionTeam>
  dq: string[]
  bonus: Record<string, number>
  points: Map<string, number>
  dispatch: React.Dispatch<import('@/lib/roundDraft').DraftAction>
}) {
  const ordinal = ['1st', '2nd', '3rd', '4th', '5th', '6th'][startSlot - 1] ?? `${startSlot}th`
  const shown = points.get(group[0])

  return (
    <li className="rounded-xl border p-2.5">
      <div className="flex items-center gap-2.5">
        <span className="w-8 shrink-0 text-xs font-bold text-muted-foreground">{ordinal}</span>

        <div className="flex min-w-0 flex-1 flex-wrap gap-1.5">
          {group.map((teamId) => {
            const team = teams.get(teamId)
            const out = dq.includes(teamId)
            return (
              <span
                key={teamId}
                className="inline-flex h-7 items-center rounded-lg px-2.5 text-sm font-bold"
                style={{
                  background: team?.colorHex,
                  color: team?.textOnColorHex,
                  textDecoration: out ? 'line-through' : undefined,
                  textDecorationThickness: out ? '2px' : undefined,
                }}
              >
                {team?.name ?? 'Unknown'}
              </span>
            )
          })}
        </div>

        {group.length === 1 && (
          <span className="shrink-0 text-lg font-bold tabular-nums">
            {shown === undefined ? '' : Math.round(shown)}
          </span>
        )}
      </div>

      <div className="mt-2 flex flex-wrap gap-1.5">
        <IconChip
          label="Move up"
          disabled={index === 0}
          onClick={() => dispatch({ type: 'move', groupIndex: index, direction: 'up' })}
        >
          <ArrowUp className="size-3.5" />
        </IconChip>

        <IconChip
          label="Move down"
          disabled={index === total - 1}
          onClick={() => dispatch({ type: 'move', groupIndex: index, direction: 'down' })}
        >
          <ArrowDown className="size-3.5" />
        </IconChip>

        {index > 0 && (
          <Chip onClick={() => dispatch({ type: 'tieUp', groupIndex: index })}>
            <ChevronsUp className="size-3.5" />
            Tie up
          </Chip>
        )}

        {/* A single team's controls sit on the same line as the ordering ones.
            A shared place puts each team on its own labelled line below, so a
            remove button is never ambiguous about who it removes. */}
        {group.length === 1 && (
          <TeamControls
            teamId={group[0]}
            dq={dq.includes(group[0])}
            bonus={bonus[group[0]] ?? 0}
            dispatch={dispatch}
          />
        )}
      </div>

      {group.length > 1 && (
        <div className="mt-2 flex flex-col gap-1.5 border-t pt-2">
          {group.map((teamId) => (
            <div key={teamId} className="flex flex-wrap items-center gap-1.5">
              <span
                className="inline-flex h-7 min-w-16 items-center justify-center rounded-md px-2 text-xs font-bold"
                style={{ background: teams.get(teamId)?.colorHex, color: teams.get(teamId)?.textOnColorHex }}
              >
                {teams.get(teamId)?.name}
              </span>
              <TeamControls
                teamId={teamId}
                dq={dq.includes(teamId)}
                bonus={bonus[teamId] ?? 0}
                dispatch={dispatch}
              />
              <span className="ml-auto text-base font-bold tabular-nums">
                {points.get(teamId) === undefined ? '' : Math.round(points.get(teamId)!)}
              </span>
            </div>
          ))}
        </div>
      )}
    </li>
  )
}

/** Disqualify, bonus and remove, for exactly one team. */
function TeamControls({
  teamId,
  dq,
  bonus,
  dispatch,
}: {
  teamId: string
  dq: boolean
  bonus: number
  dispatch: React.Dispatch<import('@/lib/roundDraft').DraftAction>
}) {
  return (
    <>
      <Chip active={dq} tone="destructive" onClick={() => dispatch({ type: 'toggleDq', teamId })}>
        DQ
      </Chip>

      {bonus === 0 ? (
        <Chip onClick={() => dispatch({ type: 'setBonus', teamId, points: 10 })}>
          <Plus className="size-3.5" />
          Bonus
        </Chip>
      ) : (
        // A stepper rather than a fixed value, because the archive shows the
        // bonus was sometimes 10 and sometimes 20, and a future game will award
        // something else again.
        <span className="inline-flex h-9 items-center gap-1 rounded-lg border px-1">
          <button
            type="button"
            aria-label="Less bonus"
            className="flex size-7 items-center justify-center rounded-md hover:bg-muted"
            onClick={() =>
              dispatch({ type: 'setBonus', teamId, points: Math.max(0, bonus - BONUS_STEP) })
            }
          >
            <Minus className="size-3.5" />
          </button>
          <span className="min-w-9 text-center text-sm font-bold tabular-nums">+{bonus}</span>
          <button
            type="button"
            aria-label="More bonus"
            className="flex size-7 items-center justify-center rounded-md hover:bg-muted"
            onClick={() => dispatch({ type: 'setBonus', teamId, points: bonus + BONUS_STEP })}
          >
            <Plus className="size-3.5" />
          </button>
        </span>
      )}

      <IconChip label="Remove from the round" onClick={() => dispatch({ type: 'remove', teamId })}>
        <X className="size-3.5" />
      </IconChip>
    </>
  )
}

function Chip({
  children,
  onClick,
  active,
  tone,
}: {
  children: React.ReactNode
  onClick: () => void
  active?: boolean
  tone?: 'destructive'
}) {
  const destructive = tone === 'destructive'

  return (
    <button
      type="button"
      onClick={onClick}
      className={[
        'inline-flex h-9 items-center gap-1.5 rounded-lg border px-2.5 text-xs font-medium transition-colors',
        active && destructive
          ? 'border-destructive bg-destructive/10 font-bold text-destructive'
          : 'border-border hover:bg-muted',
      ].join(' ')}
    >
      {children}
    </button>
  )
}

function IconChip({
  children,
  onClick,
  label,
  disabled,
}: {
  children: React.ReactNode
  onClick: () => void
  label: string
  disabled?: boolean
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      title={label}
      className="flex size-9 items-center justify-center rounded-lg border border-border transition-colors hover:bg-muted disabled:opacity-35"
    >
      {children}
    </button>
  )
}

/**
 * The explanations worth reading.
 *
 * "3rd place, 20" needs no explaining, and printing four such lines buries the
 * one that does. Ties and disqualifications are the cases where the arithmetic
 * is not obvious, and they are exactly the cases that used to be argued about.
 */
function reasoning(preview: Preview | undefined): string | null {
  if (!preview?.awards.length) return null

  const notable = [
    ...new Set(
      preview.awards
        .filter((a) => a.explanation.includes('Tied') || a.isDisqualified)
        .map((a) => a.explanation),
    ),
  ]

  if (notable.length > 0) return notable.join(' ')
  return 'Clean finish, no ties.'
}

function PreviewPanel({
  teams,
  preview,
  error,
  isPending,
  empty,
}: {
  teams: SessionTeam[]
  preview: Preview | undefined
  error: unknown
  isPending: boolean
  empty: boolean
}) {
  const byTeam = new Map(preview?.awards.map((a) => [a.teamId, a]) ?? [])

  return (
    <div className="rounded-xl border p-4">
      <div className="mb-3 flex items-baseline justify-between">
        <span className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
          Points if confirmed
        </span>
        {isPending && <span className="text-xs text-muted-foreground">Checking...</span>}
      </div>

      <div className="flex flex-col gap-2">
        {teams.map((team) => {
          const award = byTeam.get(team.teamId)
          return (
            <div key={team.teamId} className="flex items-center gap-2.5">
              <span
                className="size-2.5 shrink-0 rounded-sm"
                style={{ background: team.colorHex, opacity: award ? 1 : 0.3 }}
                aria-hidden
              />
              <span className="flex-1 text-sm font-medium">{team.name}</span>
              <span
                className={[
                  'font-bold tabular-nums',
                  award ? 'text-base' : 'text-sm font-medium text-muted-foreground',
                ].join(' ')}
              >
                {award ? Math.round(award.points) : '0'}
              </span>
            </div>
          )
        })}
      </div>

      {/* The reasoning, in words, before anything is saved. This is the direct
          answer to scoring that used to change depending on the kind of tie:
          the scorekeeper confirms points, not just an order. */}
      <p
        className={[
          'mt-3 border-t pt-3 text-xs leading-relaxed',
          error ? 'text-destructive' : 'text-muted-foreground',
        ].join(' ')}
      >
        {error
          ? error instanceof ApiError
            ? `Points unavailable: ${error.message}`
            : 'Points unavailable. Check the connection.'
          : empty
          ? 'Tap a team to see what the round would score.'
          : preview?.isValid === false
            ? preview.errors.map((e) => e.message).join(' ')
            : (reasoning(preview) ?? 'Working it out...')}
      </p>
    </div>
  )
}
