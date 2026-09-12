import { useCallback, useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { config } from '@/config'

interface Health {
  status: string
  service: string
  utc: string
}

/*
  Placeholder teams for the scaffold only.

  Real teams come from the API, including their colours, because tenants
  configure them. The inline custom property below is the pattern the whole app
  uses, so it is written the right way round from the start even while the
  values are still hardcoded here.
*/
const PLACEHOLDER_TEAMS = [
  { name: 'Red', color: 'var(--color-team-red)', textOnColor: 'var(--color-team-red-fg)' },
  { name: 'Blue', color: 'var(--color-team-blue)', textOnColor: 'var(--color-team-blue-fg)' },
  { name: 'Yellow', color: 'var(--color-team-yellow)', textOnColor: 'var(--color-team-yellow-fg)' },
  { name: 'Green', color: 'var(--color-team-green)', textOnColor: 'var(--color-team-green-fg)' },
]

type Status =
  | { state: 'loading' }
  | { state: 'ok'; health: Health }
  | { state: 'error'; message: string }

export default function App() {
  const [status, setStatus] = useState<Status>({ state: 'loading' })
  const [attempt, setAttempt] = useState(0)

  const retry = useCallback(() => {
    setStatus({ state: 'loading' })
    setAttempt((n) => n + 1)
  }, [])

  useEffect(() => {
    const controller = new AbortController()

    fetch(`${config.apiBaseUrl}/api/health`, {
      signal: controller.signal,
      credentials: 'include',
    })
      .then(async (response) => {
        if (!response.ok) throw new Error(`API returned ${response.status}`)
        setStatus({ state: 'ok', health: (await response.json()) as Health })
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        setStatus({
          state: 'error',
          message: error instanceof Error ? error.message : String(error),
        })
      })

    return () => {
      controller.abort()
    }
  }, [attempt])

  return (
    <main className="min-h-dvh bg-background px-4 py-10 text-foreground">
      <div className="mx-auto flex max-w-2xl flex-col gap-8">
        <header>
          <h1 className="text-2xl font-semibold">Awana Scoreboard</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Scaffold check. Nothing here is the real product yet.
          </p>
        </header>

        <Card>
          <CardHeader>
            <CardTitle>API connection</CardTitle>
            <CardDescription>{config.apiBaseUrl}</CardDescription>
          </CardHeader>
          <CardContent className="text-sm">
            {status.state === 'loading' && <p className="text-muted-foreground">Checking...</p>}

            {status.state === 'ok' && (
              <div className="flex items-start gap-3">
                <span
                  className="mt-1.5 size-2.5 shrink-0 rounded-full"
                  style={{ background: 'var(--color-status-live)' }}
                  aria-hidden
                />
                <div>
                  <p className="font-medium">Connected to {status.health.service}</p>
                  <p className="text-muted-foreground">Server time {status.health.utc}</p>
                </div>
              </div>
            )}

            {status.state === 'error' && (
              <div className="flex items-start gap-3">
                <span
                  className="mt-1.5 size-2.5 shrink-0 rounded-full"
                  style={{ background: 'var(--color-status-down)' }}
                  aria-hidden
                />
                <div className="flex flex-col items-start gap-3">
                  <div>
                    <p className="font-medium">Could not reach the API</p>
                    <p className="text-muted-foreground">{status.message}</p>
                    <p className="mt-1 text-muted-foreground">
                      Start it with{' '}
                      <code className="rounded bg-muted px-1 py-0.5">
                        dotnet run --project apps/api/src/Awana.Api
                      </code>
                    </p>
                  </div>
                  <Button variant="outline" size="sm" onClick={retry}>
                    Retry
                  </Button>
                </div>
              </div>
            )}
          </CardContent>
        </Card>

        <section>
          <h2 className="text-sm font-semibold tracking-wide text-muted-foreground uppercase">
            Team colour tokens
          </h2>
          <ul className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-4">
            {PLACEHOLDER_TEAMS.map((team) => (
              <li
                key={team.name}
                className="rounded-lg px-3 py-6 text-center font-semibold"
                style={{ background: team.color, color: team.textOnColor }}
              >
                {team.name}
              </li>
            ))}
          </ul>
        </section>
      </div>
    </main>
  )
}
