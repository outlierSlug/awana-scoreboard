import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArchiveRestore, ArchiveX, GripVertical, Info, Pencil, Plus, Trash2 } from 'lucide-react'
import { useRef, useState } from 'react'
import { GameDialog } from '@/components/games/GameDialog'
import { GameHelpDialog } from '@/components/games/GameHelpDialog'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { api, ApiError } from '@/lib/apiClient'
import { useMe } from '@/lib/auth'
import { queryKeys } from '@/lib/queryClient'
import { UserRole, type GameDetail, type SaveGameRequest } from '@/lib/types'

/**
 * The club's list of what it plays.
 *
 * Reference data rather than tonight's scores, and the difference shows in what
 * it protects. A game that has been played can never be deleted, because a
 * season of rounds points at it and the board has to keep reading correctly in
 * a year. Retiring is the answer instead: out of the picker, and the past left
 * exactly as it was.
 *
 * The page itself is only names in the order the scorekeeper sees them. Open
 * one and everything about it, reading and editing alike, is in the dialog.
 */
export function GamesPage() {
  const queryClient = useQueryClient()
  const { can } = useMe()

  // The API refuses these regardless. Hiding them keeps a scorekeeper from
  // filling in a form whose only possible ending is a permission error.
  const canEdit = can(UserRole.GamesLeader)
  const canDelete = can(UserRole.Admin)

  // The game the dialog is showing. 'new' is the one being typed in, which has
  // no id to point at yet.
  const [showing, setShowing] = useState<GameDetail | 'new' | null>(null)
  const [retiring, setRetiring] = useState<GameDetail | null>(null)
  const [deleting, setDeleting] = useState<GameDetail | null>(null)
  const [helping, setHelping] = useState(false)

  const games = useQuery<GameDetail[]>({
    queryKey: queryKeys.gameCatalog(),
    queryFn: ({ signal }) => api.gameCatalog(signal),
    enabled: canEdit,
  })

  /**
   * Everything that reads the game list has to be refreshed, not just this
   * page: a rename has to reach the picker on a console somebody has open in
   * another tab.
   */
  const refresh = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.gameCatalog() }),
      queryClient.invalidateQueries({ queryKey: ['games'] }),
    ])
  }

  const close = () => setShowing(null)

  const save = useMutation({
    mutationFn: ({ id, body }: { id: string | null; body: SaveGameRequest }) =>
      id === null ? api.createGame(body) : api.updateGame(id, body),
    onSuccess: async () => {
      close()
      await refresh()
    },
  })

  const setActive = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      active ? api.restoreGame(id) : api.retireGame(id),
    onSuccess: async () => {
      setRetiring(null)
      await refresh()
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteGame(id),
    onSuccess: async () => {
      setDeleting(null)
      await refresh()
    },
  })

  const reorder = useMutation({
    mutationFn: (ids: string[]) => api.reorderGames(ids),
    onSuccess: async (next) => {
      queryClient.setQueryData(queryKeys.gameCatalog(), next)
      await queryClient.invalidateQueries({ queryKey: ['games'] })
    },
  })

  const active = games.data?.filter((game) => game.isActive) ?? []
  const retired = games.data?.filter((game) => !game.isActive) ?? []

  const drag = useDragToPlace({
    games: active,
    onDrop: (next) => {
      // Retired games keep their places at the end. Only the arranged list is
      // being rewritten.
      const all = [...next, ...retired]
      queryClient.setQueryData(queryKeys.gameCatalog(), all)
      reorder.mutate(all.map((game) => game.id))
    },
  })

  if (!canEdit) {
    return (
      <EmptyState
        title="You do not have access to the game catalog"
        detail="A games leader or an admin can change what the club plays. Ask one of them if the list needs editing."
      />
    )
  }

  const editing = showing === 'new' ? null : showing

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-1.5">
          <h1 className="text-2xl font-bold tracking-tight">Games</h1>

          {/* Everything this page needs explaining is behind here, so the list
              itself can be a list. */}
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="How this page works"
            onClick={() => setHelping(true)}
          >
            <Info />
          </Button>
        </div>

        <Button size="lg" onClick={() => setShowing('new')}>
          <Plus />
          New game
        </Button>
      </div>

      {games.isPending && <div className="h-40 animate-pulse rounded-xl bg-muted" />}

      {games.error && (
        <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
          Could not load the game catalog. {(games.error as Error).message}
        </p>
      )}

      {/* A reorder that failed leaves the list showing an order the server does
          not have, so it has to be said rather than swallowed. */}
      {reorder.error instanceof ApiError && (
        <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
          The new order was not saved. {reorder.error.message}
        </p>
      )}

      {games.data && active.length === 0 && retired.length === 0 && (
        <EmptyState
          title="No games yet"
          detail="Add the games the club plays. They show up in the picker the moment a scorekeeper records a round."
        />
      )}

      {active.length > 0 && (
        <List
          games={active}
          drag={drag}
          canDelete={canDelete}
          onEdit={setShowing}
          onRetire={setRetiring}
          onDelete={setDeleting}
        />
      )}

      {retired.length > 0 && (
        <List
          title="Retired"
          games={retired}
          canDelete={canDelete}
          onEdit={setShowing}
          onRestore={(game) => setActive.mutate({ id: game.id, active: true })}
          onDelete={setDeleting}
        />
      )}

      <GameHelpDialog open={helping} onClose={() => setHelping(false)} />

      <GameDialog
        open={showing !== null}
        game={editing}
        busy={save.isPending}
        error={save.error}
        onClose={() => {
          close()
          save.reset()
        }}
        onSave={(body) => save.mutate({ id: editing?.id ?? null, body })}
      />

      {/* Retiring and deleting are decisions rather than edits, so each is its
          own question with its own consequences spelled out. They look like the
          same action and are not. */}
      <ConfirmDialog
        open={retiring !== null}
        title={`Retire ${retiring?.name ?? 'this game'}?`}
        confirmLabel="Retire it"
        busy={setActive.isPending}
        onCancel={() => setRetiring(null)}
        onConfirm={() => retiring && setActive.mutate({ id: retiring.id, active: false })}
      >
        <p>
          It leaves the scorekeeper&rsquo;s list, so no new rounds can be recorded on it. Every
          round already played on it stays exactly as it is. You can put it back at any time.
        </p>
      </ConfirmDialog>

      <ConfirmDialog
        open={deleting !== null}
        title={`Delete ${deleting?.name ?? 'this game'}?`}
        confirmLabel="Delete it"
        destructive
        busy={remove.isPending}
        onCancel={() => {
          setDeleting(null)
          remove.reset()
        }}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
      >
        <p>
          This removes it from the catalog for good and cannot be undone. It has never been
          played, so nothing in the history refers to it. To stop playing a game that has been
          played, retire it instead.
        </p>

        {remove.error instanceof ApiError && (
          <p className="mt-3 text-destructive">{remove.error.message}</p>
        )}
      </ConfirmDialog>
    </div>
  )
}

// ------------------------------------------------------------------ dragging

interface Drag {
  /** The game in hand, so its row can show it has been lifted. */
  activeId: string | null
  /** Where it would land: the index the drop indicator sits above. */
  placeAt: number | null
  handleProps: (game: GameDetail) => React.HTMLAttributes<HTMLElement>
}

/**
 * Pick a game up, and put it down somewhere.
 *
 * Deliberately not a live swap. Reordering as the pointer crosses each row
 * makes the list squirm under the hand, and what you let go of is not what you
 * aimed at, because everything shifted on the way there. So nothing moves
 * during the drag: a line shows where the row will land, and the list is
 * rewritten once, on release.
 *
 * Pointer events rather than HTML5 drag and drop, which does not fire on touch
 * at all. This page is opened on a phone as readily as a laptop.
 */
function useDragToPlace({
  games,
  onDrop,
}: {
  games: GameDetail[]
  onDrop: (games: GameDetail[]) => void
}): Drag {
  const [activeId, setActiveId] = useState<string | null>(null)
  const [placeAt, setPlaceAt] = useState<number | null>(null)

  // Read inside a pointer handler, which can fire before React has re-rendered
  // with the state it is about to read.
  const target = useRef<number | null>(null)

  /**
   * Which gap the pointer is nearest, as an index into the list.
   *
   * Measured off the rows on screen rather than tracked from the start, so it
   * stays right even if the page scrolled during the drag.
   */
  const gapUnder = (clientY: number): number => {
    const rows = [...document.querySelectorAll<HTMLElement>('[data-game-row]')]

    for (let index = 0; index < rows.length; index++) {
      const box = rows[index].getBoundingClientRect()
      if (clientY < box.top + box.height / 2) return index
    }

    return rows.length
  }

  const drop = (game: GameDetail) => {
    const gap = target.current

    setActiveId(null)
    setPlaceAt(null)
    target.current = null

    // Null means the pointer never moved, which is a click on the handle and
    // not a reorder. It happens constantly by accident.
    if (gap === null) return

    const from = games.findIndex((g) => g.id === game.id)
    if (from < 0) return

    // The gap is counted with the row still in the list, so dropping into the
    // gap just below where it already sits is where it already sits.
    const to = gap > from ? gap - 1 : gap
    if (to === from) return

    const next = [...games]
    next.splice(to, 0, ...next.splice(from, 1))
    onDrop(next)
  }

  const handleProps = (game: GameDetail) => ({
    onPointerDown: (event: React.PointerEvent<HTMLElement>) => {
      // Left button or a touch. Anything else is a context menu or a scroll.
      if (event.button !== 0) return

      event.preventDefault()
      event.currentTarget.setPointerCapture(event.pointerId)
      setActiveId(game.id)

      target.current = null
      setPlaceAt(null)
    },

    onPointerMove: (event: React.PointerEvent<HTMLElement>) => {
      if (activeId !== game.id) return

      const gap = gapUnder(event.clientY)
      target.current = gap
      setPlaceAt(gap)
    },

    onPointerUp: (event: React.PointerEvent<HTMLElement>) => {
      if (activeId !== game.id) return
      event.currentTarget.releasePointerCapture(event.pointerId)
      drop(game)
    },

    // A cancelled drag (the browser took over, the pointer left the window)
    // puts the row down where the indicator was, which is what the hand that
    // let go was aiming at.
    onPointerCancel: () => drop(game),
  })

  return { activeId, placeAt, handleProps }
}

// -------------------------------------------------------------------- layout

function List({
  title,
  games,
  drag,
  canDelete,
  onEdit,
  onRetire,
  onRestore,
  onDelete,
}: {
  /** Only the retired list is labeled. The arranged one is just the page. */
  title?: string
  games: GameDetail[]
  /** Absent on the retired list, which sits in an order nobody sees. */
  drag?: Drag
  canDelete: boolean
  onEdit: (game: GameDetail) => void
  onRetire?: (game: GameDetail) => void
  onRestore?: (game: GameDetail) => void
  onDelete: (game: GameDetail) => void
}) {
  return (
    <section>
      {title && (
        <h2 className="mb-2 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
          {title}
        </h2>
      )}

      <ul className="flex flex-col gap-1.5">
        {games.map((game, index) => (
          <Row
            key={game.id}
            game={game}
            drag={drag}
            dropBefore={drag?.placeAt === index}
            canDelete={canDelete}
            onEdit={() => onEdit(game)}
            onRetire={onRetire && (() => onRetire(game))}
            onRestore={onRestore && (() => onRestore(game))}
            onDelete={() => onDelete(game)}
          />
        ))}

        {/* The gap past the last row, so a game can be dropped at the bottom. */}
        {drag?.placeAt === games.length && <DropLine />}
      </ul>
    </section>
  )
}

function DropLine() {
  return <li aria-hidden className="-my-0.5 h-0.5 rounded-full bg-primary" />
}

function Row({
  game,
  drag,
  dropBefore,
  canDelete,
  onEdit,
  onRetire,
  onRestore,
  onDelete,
}: {
  game: GameDetail
  drag?: Drag
  dropBefore: boolean
  canDelete: boolean
  onEdit: () => void
  onRetire?: () => void
  onRestore?: () => void
  onDelete: () => void
}) {
  const carried = drag?.activeId === game.id

  return (
    <>
      {dropBefore && <DropLine />}

      <li
        data-game-id={game.id}
        // Only rows that can be arranged are measured for a drop position.
        data-game-row={drag ? '' : undefined}
        className={[
          'flex items-center gap-2 rounded-xl py-1.5 pr-2 pl-1 transition-opacity',
          game.isActive
            ? 'bg-card ring-1 ring-foreground/10'
            : 'border border-dashed border-foreground/20',
          // Lifted, not moved. The row stays put and goes pale, so the list it
          // came out of is still readable while choosing where to put it.
          carried ? 'opacity-40' : '',
        ].join(' ')}
      >
        {drag ? (
          <span
            {...drag.handleProps(game)}
            role="button"
            tabIndex={-1}
            aria-label={`Drag ${game.name} to reorder`}
            className={[
              'flex h-9 w-6 shrink-0 touch-none items-center justify-center text-muted-foreground/50',
              carried ? 'cursor-grabbing text-foreground' : 'cursor-grab hover:text-foreground',
            ].join(' ')}
          >
            <GripVertical className="size-4" />
          </span>
        ) : (
          <span className="w-6 shrink-0" />
        )}

        {/* Plain text. The row is a name with buttons beside it, not a target
            that opens something: what each button does is written on it. */}
        <span className="flex min-w-0 flex-1 items-center gap-2.5">
          <span className="truncate font-semibold">{game.name}</span>

          {/* A game with nothing written down is the one worth spotting: it is
              the gap somebody opened this page to close. */}
          {!game.notes && (
            <span className="hidden shrink-0 text-xs text-muted-foreground/70 italic sm:inline">
              no rules yet
            </span>
          )}
        </span>

        {/* Words rather than icons where there is room. This page is opened a
            few times a season by somebody who has never seen it. */}
        <span className="flex shrink-0 items-center gap-0.5">
          <Button variant="ghost" size="sm" aria-label={`Edit ${game.name}`} onClick={onEdit}>
            <Pencil />
            <span className="hidden sm:inline">Edit</span>
          </Button>

          {onRetire && (
            <Button variant="ghost" size="sm" aria-label={`Retire ${game.name}`} onClick={onRetire}>
              <ArchiveX />
              <span className="hidden sm:inline">Retire</span>
            </Button>
          )}

          {onRestore && (
            <Button
              variant="ghost"
              size="sm"
              aria-label={`Put ${game.name} back in the picker`}
              onClick={onRestore}
            >
              <ArchiveRestore />
              <span className="hidden sm:inline">Put back</span>
            </Button>
          )}

          {/* Only for a game nobody played. The API refuses the rest, and
              offering a button whose only answer is no is worse than not
              offering it: retiring is what was wanted anyway. */}
          {canDelete && game.roundCount === 0 && (
            <Button
              variant="ghost"
              size="sm"
              aria-label={`Delete ${game.name}`}
              onClick={onDelete}
              className="text-destructive hover:bg-destructive/10 hover:text-destructive"
            >
              <Trash2 />
              <span className="hidden sm:inline">Delete</span>
            </Button>
          )}
        </span>
      </li>
    </>
  )
}

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="rounded-xl border border-dashed p-8 text-center">
      <p className="font-medium">{title}</p>
      <p className="mx-auto mt-1 max-w-md text-sm leading-relaxed text-muted-foreground">
        {detail}
      </p>
    </div>
  )
}
