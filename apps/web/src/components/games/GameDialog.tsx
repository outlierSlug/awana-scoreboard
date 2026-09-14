import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/textarea'
import { ApiError } from '@/lib/apiClient'
import type { GameDetail, SaveGameRequest } from '@/lib/types'

/**
 * What a game is called, and how it goes.
 *
 * Only those two things. Retiring and deleting are decisions rather than edits,
 * so they are their own confirmations reached from the row: putting them in
 * here meant a form you opened to fix a typo also carried the button that
 * removes the game for good.
 */
export function GameDialog({
  open,
  onClose,
  game,
  busy,
  error,
  onSave,
}: {
  open: boolean
  onClose: () => void
  /** Null adds a game. Anything else edits that one. */
  game: GameDetail | null
  busy: boolean
  error: unknown
  onSave: (body: SaveGameRequest) => void
}) {
  return (
    <Modal open={open} onClose={onClose} locked={busy} className="w-[min(32rem,calc(100%-2rem))]">
      {/* Remounted with the dialog, so it opens on the game being edited
          rather than on whatever was open the last time. */}
      {open && <Form game={game} busy={busy} error={error} onClose={onClose} onSave={onSave} />}
    </Modal>
  )
}

function Form({
  game,
  busy,
  error,
  onClose,
  onSave,
}: {
  game: GameDetail | null
  busy: boolean
  error: unknown
  onClose: () => void
  onSave: (body: SaveGameRequest) => void
}) {
  const [name, setName] = useState(game?.name ?? '')
  const [notes, setNotes] = useState(game?.notes ?? '')

  const ready = name.trim() !== '' && !busy

  const submit = () => {
    if (!ready) return
    onSave({
      name: name.trim(),
      notes: notes.trim() === '' ? null : notes.trim(),
    })
  }

  return (
    <div className="flex flex-col gap-5 p-5">
      <div>
        <h2 className="text-lg font-bold tracking-tight">{game ? game.name : 'New game'}</h2>

        {/* No round count here. How often a game gets played is week to week
            tracking, which is its own screen; this one says what the game is.
            That it is retired still belongs, because the dialog is opened away
            from the list section that would otherwise say so. */}
        {game && !game.isActive && (
          <p className="mt-0.5 text-sm text-muted-foreground">Retired</p>
        )}
      </div>

      <div>
        <Label htmlFor="game-name">Name</Label>
        <input
          id="game-name"
          type="text"
          value={name}
          maxLength={120}
          autoFocus={game === null}
          placeholder="Baton Relay"
          onChange={(event) => setName(event.target.value)}
          onKeyDown={(event) => event.key === 'Enter' && submit()}
          className="h-11 w-full rounded-lg border border-border bg-background px-3 text-sm font-medium outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
        />
      </div>

      <div>
        <Label htmlFor="game-notes">Rules</Label>
        <Textarea
          id="game-notes"
          value={notes}
          maxLength={2000}
          rows={6}
          placeholder="How the game is run, and how you decide the finishing order."
          onChange={(event) => setNotes(event.target.value)}
          className="resize-none"
        />
      </div>

      {error instanceof ApiError && <p className="text-sm text-destructive">{error.message}</p>}

      <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <Button variant="outline" size="lg" disabled={busy} onClick={onClose}>
          Cancel
        </Button>
        <Button size="lg" disabled={!ready} onClick={submit}>
          {busy ? 'Saving...' : game ? 'Save changes' : 'Add game'}
        </Button>
      </div>
    </div>
  )
}

function Label({ htmlFor, children }: { htmlFor?: string; children: React.ReactNode }) {
  return (
    <label
      htmlFor={htmlFor}
      className="mb-2 block text-xs font-semibold tracking-wide text-muted-foreground uppercase"
    >
      {children}
    </label>
  )
}
