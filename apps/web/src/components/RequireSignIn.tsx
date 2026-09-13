import { Navigate, useLocation } from 'react-router'
import { useMe } from '@/lib/auth'

/**
 * Gates the console.
 *
 * The server is the real gate and refuses the calls regardless. This exists so
 * that a signed-out visitor gets the sign-in page instead of a console that
 * fills with permission errors as each panel loads.
 */
export function RequireSignIn({ children }: { children: React.ReactNode }) {
  const { isLoading, isSignedIn } = useMe()
  const location = useLocation()

  if (isLoading) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-6">
        <div className="h-40 animate-pulse rounded-xl bg-muted" />
      </div>
    )
  }

  if (!isSignedIn) {
    // Carried so that signing in returns to the page that was asked for, not
    // to a generic landing page.
    return <Navigate to="/login" state={{ from: location.pathname + location.search }} replace />
  }

  return <>{children}</>
}
