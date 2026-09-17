import { LogOut } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { useMe, useSignOut } from '@/lib/auth'
import { roleLabel } from '@/lib/roles'
import type { Me } from '@/lib/types'

/**
 * Who is signed in, and the way out.
 *
 * A button the width of its own initials rather than an address across the
 * header. The address is still the thing that answers "is this my account or
 * the last person's", which a shared laptop makes a real question, so it is one
 * click away rather than gone.
 *
 * There is deliberately no profile page behind this. Nobody edits their own
 * account, since roles are granted by an admin on the People page, so a page
 * of your own would be a route and a heading wrapped around the same three
 * lines this panel already shows.
 */
export function AccountMenu() {
  const { me } = useMe()
  const signOut = useSignOut()
  const [open, setOpen] = useState(false)

  if (!me?.isSignedIn) return null

  const name = me.displayName ?? me.email ?? 'Signed in'
  const role = roleLabel(me.role)

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <button
          type="button"
          aria-label={`Account: ${name}`}
          className="flex size-8 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-semibold text-foreground transition-colors hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          {initials(me)}
        </button>
      </PopoverTrigger>

      <PopoverContent align="end" className="w-64 gap-0 p-0">
        <div className="flex flex-col gap-0.5 border-b p-3">
          <span className="truncate text-sm font-semibold">{name}</span>

          {/* Only when it adds something. Before anyone has signed in through
              Google the display name IS the address, and printing it twice
              reads as a rendering fault. */}
          {me.email && me.email !== name && (
            <span className="truncate text-xs text-muted-foreground">{me.email}</span>
          )}

          {/* Worth a line of its own: it is the answer to "why can I not see
              the button everyone is telling me to press". */}
          {role && <span className="mt-1 text-xs text-muted-foreground">{role}</span>}
        </div>

        <div className="p-1.5">
          <Button
            variant="ghost"
            className="h-9 w-full justify-start gap-2 font-normal"
            onClick={() => {
              setOpen(false)
              void signOut()
            }}
          >
            <LogOut className="size-4" />
            Sign out
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}

/**
 * One or two letters for the button face.
 *
 * Two words give their first letters and one word gives its first letter.
 *
 * The @ is cut off first, and not only for the email field: until somebody has
 * signed in through Google their display name IS their address, because an
 * allowlist is all addresses. Leaving the domain on turns
 * "someone@gmail.com" into the two words "someone@gmail" and "com", and the
 * button then reads SC, which looks like a name nobody has.
 */
function initials(me: Me): string {
  const source = (me.displayName?.trim() || me.email || '').split('@')[0]

  const words = source.split(/[\s._-]+/).filter(Boolean)
  if (words.length === 0) return '?'

  const letters =
    words.length === 1 ? words[0].slice(0, 1) : words[0].slice(0, 1) + words[1].slice(0, 1)

  return letters.toUpperCase()
}
