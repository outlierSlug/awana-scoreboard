import { useEffect, useState } from 'react'
import { config } from './config'

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

type Status = { state: 'loading' } | { state: 'ok'; health: Health } | { state: 'error'; message: string }

export default function App() {
  const [status, setStatus] = useState<Status>({ state: 'loading' })

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
        setStatus({ state: 'error', message: error instanceof Error ? error.message : String(error) })
      })

    return () => { controller.abort() }
  }, [])

  return (
    <main className="min-h-dvh bg-app-bg px-4 py-10 text-app-fg">
      <div className="mx-auto flex max-w-2xl flex-col gap-8">
        <header>
          <h1 className="text-2xl font-semibold">Awana Scoreboard</h1>
          <p className="mt-1 text-sm text-app-muted">
            Scaffold check. Nothing here is the real product yet.
          </p>
        </header>

        <section className="rounded-lg border border-app-border bg-white p-4">
          <h2 className="text-sm font-semibold uppercase tracking-wide text-app-muted">
            API connection
          </h2>
          <div className="mt-3 text-sm">
            {status.state === 'loading' && <p>Checking {config.apiBaseUrl}...</p>}

            {status.state === 'ok' && (
              <div className="flex items-start gap-3">
                <span
                  className="mt-1.5 size-2.5 shrink-0 rounded-full"
                  style={{ background: 'var(--color-status-live)' }}
                  aria-hidden
                />
                <div>
                  <p className="font-medium">Connected to {status.health.service}</p>
                  <p className="text-app-muted">Server time {status.health.utc}</p>
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
                <div>
                  <p className="font-medium">Could not reach {config.apiBaseUrl}</p>
                  <p className="text-app-muted">{status.message}</p>
                  <p className="mt-1 text-app-muted">
                    Start it with <code>dotnet run --project apps/api/src/Awana.Api</code>
                  </p>
                </div>
              </div>
            )}
          </div>
        </section>

        <section>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-app-muted">
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
