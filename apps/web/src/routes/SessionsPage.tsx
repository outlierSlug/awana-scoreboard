import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarPlus, ChevronRight, ExternalLink } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { api, ApiError } from '@/lib/apiClient'
import { queryKeys } from '@/lib/queryClient'
import { SessionStatus, type Division, type SessionSummary } from '@/lib/types'
import { SessionStatusLabel } from '@/components/SessionStatusLabel'
import { formatDate } from '@/lib/format'

export function SessionsPage() {
  const queryClient = useQueryClient()
  const [creating, setCreating] = useState(false)

  const sessions = useQuery<SessionSummary[]>({
    queryKey: queryKeys.sessions(),
    queryFn: ({ signal }) => api.sessions(signal),
  })

  const divisions = useQuery<Division[]>({
    queryKey: queryKeys.divisions(),
    queryFn: ({ signal }) => api.divisions(signal),
    staleTime: Infinity,
  })

  const create = useMutation({
    mutationFn: ({ divisionId, date }: { divisionId: string; date: string }) =>
      api.createSession(divisionId, date),
    onSuccess: async () => {
      setCreating(false)
      await queryClient.invalidateQueries({ queryKey: queryKeys.sessions() })
    },
  })

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Sessions</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            One session per division per night.
          </p>
        </div>

        <Button size="lg" onClick={() => setCreating((open) => !open)}>
          <CalendarPlus />
          New session
        </Button>
      </div>

      {creating && divisions.data && (
        <NewSessionForm
          divisions={divisions.data}
          pending={create.isPending}
          error={create.error}
          onCancel={() => setCreating(false)}
          onSubmit={(divisionId, date) => create.mutate({ divisionId, date })}
        />
      )}

      {sessions.isPending && <div className="h-24 animate-pulse rounded-xl bg-muted" />}

      {sessions.error && (
        <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
          Could not load sessions. {(sessions.error as Error).message}
        </p>
      )}

      {sessions.data?.length === 0 && (
        <div className="rounded-xl border border-dashed p-10 text-center">
          <p className="font-medium">No sessions yet.</p>
          <p className="mt-1 text-sm text-muted-foreground">
            Create one for tonight and start it when the games begin.
          </p>
        </div>
      )}

      <ul className="flex flex-col gap-2">
        {sessions.data?.map((session) => (
          <li
            key={session.id}
            className="flex items-center gap-3 rounded-xl bg-card p-4 ring-1 ring-foreground/10"
          >
            <Link to={`/app/sessions/${session.id}`} className="min-w-0 flex-1">
              <span className="flex items-center gap-2.5">
                <span className="font-semibold">{session.divisionName}</span>
                <SessionStatusLabel status={session.status} />
              </span>
              <span className="mt-0.5 block text-sm text-muted-foreground">
                {formatDate(session.date)} · {session.roundCount}{' '}
                {session.roundCount === 1 ? 'round' : 'rounds'}
              </span>
            </Link>

            {session.status !== SessionStatus.Setup && (
              <Button asChild variant="ghost" size="sm">
                <a href={`/board/${session.slug}`} target="_blank" rel="noreferrer">
                  Board
                  <ExternalLink />
                </a>
              </Button>
            )}

            <Button asChild variant="outline" size="sm">
              <Link to={`/app/sessions/${session.id}`}>
                Open
                <ChevronRight />
              </Link>
            </Button>
          </li>
        ))}
      </ul>
    </div>
  )
}

function NewSessionForm({
  divisions,
  pending,
  error,
  onCancel,
  onSubmit,
}: {
  divisions: Division[]
  pending: boolean
  error: unknown
  onCancel: () => void
  onSubmit: (divisionId: string, date: string) => void
}) {
  const [divisionId, setDivisionId] = useState(divisions[0]?.id ?? '')
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))

  return (
    <form
      className="flex flex-col gap-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10"
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit(divisionId, date)
      }}
    >
      <div className="flex flex-col gap-4 sm:flex-row">
        <label className="flex flex-1 flex-col gap-1.5">
          <span className="text-sm font-medium">Division</span>
          <div className="flex gap-2">
            {divisions.map((division) => (
              <button
                key={division.id}
                type="button"
                onClick={() => setDivisionId(division.id)}
                className={[
                  'h-11 flex-1 rounded-lg border text-sm font-semibold transition-colors',
                  division.id === divisionId
                    ? 'border-foreground bg-primary text-primary-foreground'
                    : 'border-border bg-background hover:bg-muted',
                ].join(' ')}
              >
                {division.name}
              </button>
            ))}
          </div>
        </label>

        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-medium">Date</span>
          <input
            type="date"
            value={date}
            onChange={(event) => setDate(event.target.value)}
            className="h-11 rounded-lg border border-border bg-background px-3 text-sm"
            required
          />
        </label>
      </div>

      {error instanceof ApiError && (
        <p className="text-sm text-destructive">{error.message}</p>
      )}

      <div className="flex gap-2">
        <Button type="submit" size="lg" disabled={pending || !divisionId}>
          {pending ? 'Creating...' : 'Create session'}
        </Button>
        <Button type="button" variant="ghost" size="lg" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
