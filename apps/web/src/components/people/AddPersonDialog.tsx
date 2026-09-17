import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import { ApiError } from '@/lib/apiClient'
import { ROLE_LABEL, ROLE_SUMMARY, ROLES_BY_ACCESS } from '@/lib/roles'
import { UserRole } from '@/lib/types'

/**
 * Gives an address access, with a role.
 *
 * Scorekeeper is chosen to begin with because it is what almost every leader
 * asking for access actually needs, and granting more later is one change on
 * the list. Starting from the least that works is the safer mistake.
 */
export function AddPersonDialog({
  open,
  onClose,
  busy,
  error,
  onAdd,
}: {
  open: boolean
  onClose: () => void
  busy: boolean
  error: unknown
  onAdd: (email: string, role: UserRole) => void
}) {
  return (
    <Modal open={open} onClose={onClose} locked={busy}>
      {/* Remounted with the dialog, so it opens empty rather than holding the
          last address somebody typed. */}
      <Form busy={busy} error={error} onClose={onClose} onAdd={onAdd} />
    </Modal>
  )
}

function Form({
  busy,
  error,
  onClose,
  onAdd,
}: {
  busy: boolean
  error: unknown
  onClose: () => void
  onAdd: (email: string, role: UserRole) => void
}) {
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<UserRole>(UserRole.Scorekeeper)

  // Only the shape a browser can check. The server decides for real, and says
  // why in a sentence when it refuses.
  const looksLikeEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())
  const ready = looksLikeEmail && !busy

  const submit = () => {
    if (ready) onAdd(email.trim(), role)
  }

  return (
    <div className="flex flex-col gap-5 p-5">
      <h2 className="text-lg font-bold tracking-tight">Add a person</h2>

      <div>
        <Label htmlFor="person-email">Google account email</Label>
        <input
          id="person-email"
          type="email"
          inputMode="email"
          autoComplete="off"
          autoCapitalize="off"
          spellCheck={false}
          value={email}
          maxLength={320}
          autoFocus
          placeholder="leader@gmail.com"
          onChange={(event) => setEmail(event.target.value)}
          onKeyDown={(event) => event.key === 'Enter' && submit()}
          className="h-11 w-full rounded-lg border border-border bg-background px-3 text-sm font-medium outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
        />
        <p className="mt-2 text-xs text-muted-foreground">
          The address they sign in to Google with. Anyone not added here is turned away at sign-in.
        </p>
      </div>

      <div>
        <Label>Role</Label>
        <div className="flex flex-col gap-2" role="radiogroup" aria-label="Role">
          {ROLES_BY_ACCESS.map((option) => {
            const selected = option === role

            return (
              <button
                key={option}
                type="button"
                role="radio"
                aria-checked={selected}
                onClick={() => setRole(option)}
                className={[
                  'flex flex-col items-start rounded-lg border px-3 py-2.5 text-left transition-colors',
                  selected ? 'border-foreground bg-muted' : 'border-border bg-background hover:bg-muted/60',
                ].join(' ')}
              >
                <span className="text-sm font-semibold">{ROLE_LABEL[option]}</span>
                <span className="text-xs text-muted-foreground">{ROLE_SUMMARY[option]}</span>
              </button>
            )
          })}
        </div>
      </div>

      {error instanceof ApiError && <p className="text-sm text-destructive">{error.message}</p>}

      <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <Button variant="outline" size="lg" disabled={busy} onClick={onClose}>
          Cancel
        </Button>
        <Button size="lg" disabled={!ready} onClick={submit}>
          {busy ? 'Adding...' : `Add as ${ROLE_LABEL[role].toLowerCase()}`}
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
