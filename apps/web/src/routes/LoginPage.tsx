import { ArrowLeft, ClipboardList, ShieldAlert } from 'lucide-react'
import { Link, useLocation } from 'react-router'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { useMe } from '@/lib/auth'
import { api } from '@/lib/apiClient'

/**
 * The sign-in page, and the page that says no.
 *
 * One component for both, because they are the same card with a different
 * answer in it, and the denied case is reached by a redirect from the API
 * rather than by anything the app decides.
 */
export function LoginPage({ denied = false }: { denied?: boolean }) {
  const location = useLocation()
  const { isSignedIn } = useMe()

  // Where to come back to, set by the guard when it turned someone away from a
  // page they had asked for.
  const from = (location.state as { from?: string } | null)?.from
  const returnUrl = `${window.location.origin}${from ?? '/app/sessions'}`

  return (
    <div className="flex min-h-dvh flex-col bg-muted/40 text-foreground">
      <header className="mx-auto w-full max-w-5xl px-5 py-6">
        <Link to="/" className="inline-flex items-center gap-2 font-semibold">
          <ClipboardList className="size-5" />
          Awana Scoreboard
        </Link>
      </header>

      <main className="flex flex-1 items-center justify-center px-5 pb-16">
        <Card className="w-full max-w-sm">
          <CardHeader>
            {denied && (
              <span className="mb-1 inline-flex size-10 items-center justify-center rounded-full bg-destructive/10">
                <ShieldAlert className="size-5 text-destructive" />
              </span>
            )}

            <CardTitle className="text-xl">
              {denied ? 'That account is not on the list' : 'Scorekeeper sign-in'}
            </CardTitle>

            <CardDescription className="leading-relaxed">
              {denied
                ? 'Sign in again to pick a different account, or ask an admin to add this address. Only verified accounts can access the scorekeeper console.'
                : isSignedIn
                  ? 'You are already signed in.'
                  : 'Sign-in with a verified account to access the scorekeeper console.'}
            </CardDescription>
          </CardHeader>

          <CardContent className="flex flex-col gap-2">
            {!isSignedIn && (
              // A real navigation, not a fetch: the browser has to go to Google
              // and be handed back.
              <Button asChild size="lg" variant="outline" className="h-11 w-full">
                <a href={api.loginUrl(returnUrl)}>
                  <GoogleMark />
                  {/* Named for what it is for here. "Continue with Google" on
                      the page that just refused a Google account reads as the
                      same door that was locked a second ago, which is why
                      somebody turned away presses it and expects nothing. */}
                  {denied ? 'Try a different account' : 'Continue with Google'}
                </a>
              </Button>
            )}

            {isSignedIn && (
              <Button asChild size="lg" className="h-11 w-full">
                <Link to="/app/sessions">Go to the console</Link>
              </Button>
            )}
          </CardContent>

          <CardFooter className="border-t pt-4">
            <Button asChild variant="ghost" size="sm" className="w-full justify-start">
              <Link to="/">
                <ArrowLeft />
                Back
              </Link>
            </Button>
          </CardFooter>
        </Card>
      </main>
    </div>
  )
}

/**
 * Google's mark, inline.
 *
 * Drawn here rather than fetched, because the page's whole job is to be
 * reachable and a sign-in button that waits on a third-party asset is the one
 * button that must not.
 */
function GoogleMark() {
  return (
    <svg viewBox="0 0 48 48" aria-hidden className="size-4">
      <path
        fill="#EA4335"
        d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"
      />
      <path
        fill="#4285F4"
        d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"
      />
      <path
        fill="#FBBC05"
        d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"
      />
      <path
        fill="#34A853"
        d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"
      />
    </svg>
  )
}
