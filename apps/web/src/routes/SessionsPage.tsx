import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarPlus, ChevronRight, ExternalLink, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { NewSessionDialog } from '@/components/round/NewSessionDialog'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { useMe } from '@/lib/auth'
import { api, ApiError } from '@/lib/apiClient'
import { queryKeys } from '@/lib/queryClient'
import { SessionStatus, UserRole, type Division, type SessionSummary } from '@/lib/types'
import { SessionStatusLabel } from '@/components/SessionStatusLabel'
import { formatDate } from '@/lib/format'

export function SessionsPage() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)

  // The API refuses these regardless. Hiding them keeps a scorekeeper from
  // filling in a form whose only possible ending is a permission error.
  const { can } = useMe()
  const canCreate = can(UserRole.GamesLeader)
  const canDelete = can(UserRole.Admin)

  const [deleting, setDeleting] = useState<SessionSummary | null>(null)

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
    mutationFn: async ({
      divisionId,
      date,
      start,
    }: {
      divisionId: string
      date: string
      start: boolean
    }) => {
      const session = await api.createSession(divisionId, date)

      // Two calls rather than one, so both reuse an endpoint that is already
      // tested. If the second fails the session simply stays in setup, which
      // is a state it is allowed to be in and is visible in the list.
      if (start) await api.startSession(session.id)

      return { session, start }
    },
    onSuccess: async ({ session, start }) => {
      setCreating(false)
      await queryClient.invalidateQueries({ queryKey: queryKeys.sessions() })

      // Started means the games are about to begin, so the next thing wanted
      // is the console, not the list that was just left.
      if (start) navigate(`/app/sessions/${session.id}`)
    },
  })

  const remove = useMutation({
    mutationFn: (session: SessionSummary) => api.deleteSession(session.id),
    onSuccess: async () => {
      setDeleting(null)
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

        {canCreate && (
          <Button size="lg" onClick={() => setCreating(true)}>
            <CalendarPlus />
            New session
          </Button>
        )}
      </div>

      {canCreate && divisions.data && (
        <NewSessionDialog
          open={creating}
          onClose={() => setCreating(false)}
          divisions={divisions.data}
          existing={sessions.data ?? []}
          busy={create.isPending}
          error={create.error}
          onCreate={(divisionId, date, start) => create.mutate({ divisionId, date, start })}
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
            Make one for tonight and start it straight away.
          </p>
        </div>
      )}

      <ConfirmDialog
        open={deleting !== null}
        title="Delete this session?"
        confirmLabel="Delete it"
        destructive
        busy={remove.isPending}
        onCancel={() => setDeleting(null)}
        onConfirm={() => deleting && remove.mutate(deleting)}
      >
        <p>
          {deleting?.divisionName} on {deleting && formatDate(deleting.date)} has no rounds
          standing against it. Any that were already cleared go with it, and the record of who
          did what stays in the audit log.
        </p>

        {/* A session that is not empty after all still refuses, and the reason
            has to be readable rather than a button that quietly does nothing. */}
        {remove.error instanceof ApiError && (
          <p className="mt-3 rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-destructive">
            {remove.error.message}
          </p>
        )}
      </ConfirmDialog>

      <ul className="flex flex-col gap-2">
        {sessions.data?.map((session) => (
          <li
            key={session.id}
            className="flex items-center gap-3 rounded-xl bg-card p-4 ring-1 ring-foreground/10"
          >
            {/* Text, not a target. The row already carries Open and Board, and
                a card that is also one big link gives the same journey two
                affordances with different hit areas. */}
            <div className="min-w-0 flex-1">
              <span className="flex items-center gap-2.5">
                <span className="font-semibold">{session.divisionName}</span>
                <SessionStatusLabel status={session.status} />
              </span>
              <span className="mt-0.5 block text-sm text-muted-foreground">
                {formatDate(session.date)} · {session.roundCount}{' '}
                {session.roundCount === 1 ? 'round' : 'rounds'}
              </span>
            </div>

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

            {/* Only offered where it can work: the API refuses a session with
                anything recorded against it, and a button that always fails is
                worse than no button. */}
            {canDelete && session.roundCount === 0 && (
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={`Delete the ${session.divisionName} session`}
                disabled={remove.isPending}
                onClick={() => setDeleting(session)}
              >
                <Trash2 />
              </Button>
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}



