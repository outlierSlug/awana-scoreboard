import { ClipboardList, LogOut } from 'lucide-react'
import { Link, NavLink, Outlet } from 'react-router'
import { ConnectionDot } from '@/components/ConnectionDot'
import { ThemeToggle } from '@/components/ThemeToggle'
import { Button } from '@/components/ui/button'
import { useMe, useSignOut } from '@/lib/auth'
import { useHub } from '@/lib/signalr/hubContext'

/**
 * The signed-in shell.
 *
 * Deliberately its own branch of the route tree from the day it exists, so that
 * when authorization arrives it attaches here and the public board is visibly
 * outside it rather than protected by accident.
 */
export function AppShell() {
  const { state, lastMessageAt } = useHub()
  const { me } = useMe()
  const signOut = useSignOut()

  return (
    <div className="flex min-h-dvh flex-col bg-background text-foreground">
      <header className="sticky top-0 z-10 border-b bg-background/95 backdrop-blur">
        <div className="mx-auto flex h-14 max-w-5xl items-center gap-4 px-4">
          <Link to="/" className="flex shrink-0 items-center gap-2 font-semibold">
            <ClipboardList className="size-5" />
            {/* The wordmark is the first thing to go on a narrow phone. The
                icon still gets you home, and the screen title says where you
                are. */}
            <span className="hidden sm:inline">Awana Scoreboard</span>
          </Link>

          <nav className="flex items-center gap-1">
            <ShellLink to="/app/sessions">Sessions</ShellLink>
          </nav>

          <div className="ml-auto flex min-w-0 items-center gap-3">
            <ConnectionDot
              className="min-w-0 text-xs text-muted-foreground"
              labelClassName="hidden truncate sm:inline"
              state={state}
              lastMessageAt={lastMessageAt}
            />

            {/* Who is recording tonight. Worth saying out loud, because the
                rounds carry that name and a shared laptop is the normal case. */}
            {me?.displayName && (
              <span className="hidden max-w-40 truncate text-xs text-muted-foreground lg:inline">
                {me.displayName}
              </span>
            )}

            <ThemeToggle />

            {me?.isSignedIn && (
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label="Sign out"
                title="Sign out"
                onClick={signOut}
              >
                <LogOut />
              </Button>
            )}
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6">
        <Outlet />
      </main>
    </div>
  )
}

function ShellLink({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        [
          'rounded-lg px-2.5 py-1.5 text-sm font-medium transition-colors',
          isActive ? 'bg-muted text-foreground' : 'text-muted-foreground hover:text-foreground',
        ].join(' ')
      }
    >
      {children}
    </NavLink>
  )
}
