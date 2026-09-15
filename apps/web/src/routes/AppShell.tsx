import { ClipboardList, LogOut } from 'lucide-react'
import { Link, NavLink, Outlet, useLocation } from 'react-router'
import { ConnectionDot } from '@/components/ConnectionDot'
import { ErrorBoundary } from '@/components/ErrorBoundary'
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
  const location = useLocation()
  const { me } = useMe()
  const signOut = useSignOut()

  return (
    <div className="flex min-h-dvh flex-col bg-background text-foreground">
      <header className="sticky top-0 z-10 border-b bg-background/95 backdrop-blur">
        {/* Tighter at phone width, where three nav items plus the controls on
            the right already run past a 390px screen. Overflowing here is not a
            local problem: it widens the document, and a dialog sized in
            percentages then inherits the wider page and hangs off both edges. */}
        <div className="mx-auto flex h-14 max-w-5xl items-center gap-2 px-4 sm:gap-4">
          <Link to="/" className="flex shrink-0 items-center gap-2 font-semibold">
            <ClipboardList className="size-5" />
            {/* The wordmark is the first thing to go on a narrow phone. The
                icon still gets you home, and the screen title says where you
                are. */}
            <span className="hidden sm:inline">Awana Scoreboard</span>
          </Link>

          {/* Scrolls rather than pushes, so adding a fourth destination later
              cannot break the layout again. The scrollbar is hidden: this is a
              row of three links, not a scroll region anybody should see. */}
          <nav className="flex min-w-0 items-center gap-0.5 overflow-x-auto [scrollbar-width:none] sm:gap-1 [&::-webkit-scrollbar]:hidden">
            <ShellLink to="/app/sessions">Sessions</ShellLink>
            {/* Editing the catalog takes a games leader, and the page says so
                rather than vanishing: a scorekeeper who cannot find Games at
                all has no way to learn who to ask. */}
            <ShellLink to="/app/games">Games</ShellLink>
            {/* Visible to everyone signed in, like Games: the page itself says
                who can change what, which a scorekeeper cannot learn from a
                link that is simply not there. */}
            <ShellLink to="/app/scoring">Scoring</ShellLink>
          </nav>

          <div className="ml-auto flex shrink-0 items-center gap-1.5 sm:gap-3">
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
        {/* Keyed on the path, so moving to another screen clears a failure
            rather than carrying it along. */}
        <ErrorBoundary key={location.pathname}>
          <Outlet />
        </ErrorBoundary>
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
          'shrink-0 rounded-lg px-2 py-1.5 text-sm font-medium transition-colors sm:px-2.5',
          isActive ? 'bg-muted text-foreground' : 'text-muted-foreground hover:text-foreground',
        ].join(' ')
      }
    >
      {children}
    </NavLink>
  )
}
