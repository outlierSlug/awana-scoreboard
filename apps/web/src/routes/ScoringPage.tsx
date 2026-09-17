import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArchiveRestore, ArchiveX, Copy, Info, Lock, Pencil, Plus, Star, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { ScoringHelpDialog } from '@/components/scoring/ScoringHelpDialog'
import { ScoringRulesDialog } from '@/components/scoring/ScoringRulesDialog'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { api, ApiError } from '@/lib/apiClient'
import { useMe } from '@/lib/auth'
import { queryKeys } from '@/lib/queryClient'
import { UserRole, type SaveScoringProfileRequest, type ScoringProfile } from '@/lib/types'

/**
 * The named sets of scoring rules a session can run under.
 *
 * Editing these is safe for history by construction, which is the fact the
 * whole screen rests on: a session takes its own copy of the rules when it
 * starts, so nothing here can reach a night already played. That is why editing
 * asks no scary questions and why the only guarded action is deleting a set
 * some session was started from.
 */
export function ScoringPage() {
  const queryClient = useQueryClient()
  const { can } = useMe()

  // A scorekeeper can open the rules, because they are the one asked why a tie
  // scored the way it did. Deciding what the rules ARE is an admin's.
  const canRead = can(UserRole.Scorekeeper)
  const canEdit = can(UserRole.Admin)

  const [showing, setShowing] = useState<ScoringProfile | 'new' | null>(null)
  const [retiring, setRetiring] = useState<ScoringProfile | null>(null)
  const [deleting, setDeleting] = useState<ScoringProfile | null>(null)
  const [helping, setHelping] = useState(false)

  const profiles = useQuery<ScoringProfile[]>({
    queryKey: queryKeys.scoringProfiles(),
    queryFn: ({ signal }) => api.scoringProfiles(signal),
    enabled: canRead,
  })

  const refresh = () =>
    queryClient.invalidateQueries({ queryKey: queryKeys.scoringProfiles() })

  const close = () => setShowing(null)

  const save = useMutation({
    mutationFn: ({ id, body }: { id: string | null; body: SaveScoringProfileRequest }) =>
      id === null ? api.createScoringProfile(body) : api.updateScoringProfile(id, body),
    onSuccess: async () => {
      close()
      await refresh()
    },
  })

  const setDefault = useMutation({
    mutationFn: (id: string) => api.setDefaultScoringProfile(id),
    onSuccess: async () => await refresh(),
  })

  const setActive = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      active ? api.restoreScoringProfile(id) : api.retireScoringProfile(id),
    onSuccess: async () => {
      setRetiring(null)
      await refresh()
    },
  })

  const duplicate = useMutation({
    mutationFn: (id: string) => api.duplicateScoringProfile(id),
    onSuccess: async (copy) => {
      await refresh()
      // Straight into the copy, because duplicating is never the goal: it is
      // the step before changing something.
      setShowing(copy)
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteScoringProfile(id),
    onSuccess: async () => {
      setDeleting(null)
      await refresh()
    },
  })

  if (!canRead) {
    return (
      <EmptyState
        title="You do not have access to the scoring rules"
        detail="An admin decides how rounds are scored. Ask one of them if something needs changing."
      />
    )
  }

  const editing = showing === 'new' ? null : showing
  const active = profiles.data?.filter((p) => p.isActive) ?? []
  const retired = profiles.data?.filter((p) => !p.isActive) ?? []

  const failure = [setDefault.error, setActive.error, duplicate.error].find(
    (e) => e instanceof ApiError,
  )

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-1.5">
          <h1 className="text-2xl font-bold tracking-tight">Scoring</h1>

          {/* Everything this page needs explaining is behind here, so the list
              itself can be a list. */}
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="How scoring rules work"
            onClick={() => setHelping(true)}
          >
            <Info />
          </Button>
        </div>

        {canEdit && (
          <Button size="lg" onClick={() => setShowing('new')}>
            <Plus />
            New rules
          </Button>
        )}
      </div>

      {profiles.isPending && <div className="h-32 animate-pulse rounded-xl bg-muted" />}

      {profiles.error && (
        <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
          Could not load the scoring rules. {(profiles.error as Error).message}
        </p>
      )}

      {failure && (
        <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
          {(failure as ApiError).message}
        </p>
      )}

      <ul className="flex flex-col gap-2">
        {active.map((profile) => (
          <Row
            key={profile.id}
            profile={profile}
            canEdit={canEdit}
            onEdit={() => setShowing(profile)}
            onDuplicate={() => duplicate.mutate(profile.id)}
            onMakeDefault={() => setDefault.mutate(profile.id)}
            onRetire={() => setRetiring(profile)}
            onDelete={() => setDeleting(profile)}
          />
        ))}
      </ul>

      {retired.length > 0 && (
        <section>
          <h2 className="mb-2 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
            Retired
          </h2>

          <ul className="flex flex-col gap-2">
            {retired.map((profile) => (
              <Row
                key={profile.id}
                profile={profile}
                canEdit={canEdit}
                onEdit={() => setShowing(profile)}
                onDuplicate={() => duplicate.mutate(profile.id)}
                onRestore={() => setActive.mutate({ id: profile.id, active: true })}
                onDelete={() => setDeleting(profile)}
              />
            ))}
          </ul>
        </section>
      )}

      <ScoringHelpDialog open={helping} onClose={() => setHelping(false)} />

      {/* For everyone who can read, not only admins. It used to render for
          admins alone, so View on this page set state for a dialog that did
          not exist, and pressing it simply did nothing. */}
      {canRead && (
        <ScoringRulesDialog
          open={showing !== null}
          profile={editing}
          readOnly={!canEdit}
          busy={save.isPending}
          error={save.error}
          onClose={() => {
            close()
            save.reset()
          }}
          onSave={(body) => save.mutate({ id: editing?.id ?? null, body })}
        />
      )}

      <ConfirmDialog
        open={retiring !== null}
        title={`Retire ${retiring?.name ?? 'these rules'}?`}
        confirmLabel="Retire them"
        busy={setActive.isPending}
        onCancel={() => setRetiring(null)}
        onConfirm={() => retiring && setActive.mutate({ id: retiring.id, active: false })}
      >
        <p>
          They stop being offered when a session is created. Every session that already ran on
          them is untouched and still scores the same way. You can put them back at any time.
        </p>
      </ConfirmDialog>

      <ConfirmDialog
        open={deleting !== null}
        title={`Delete ${deleting?.name ?? 'these rules'}?`}
        confirmLabel="Delete"
        destructive
        busy={remove.isPending}
        onCancel={() => {
          setDeleting(null)
          remove.reset()
        }}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
      >
        <p>
          This removes them for good and cannot be undone. No session has been started on them, so
          nothing refers back to them.
        </p>

        {remove.error instanceof ApiError && (
          <p className="mt-3 text-destructive">{(remove.error as ApiError).message}</p>
        )}
      </ConfirmDialog>
    </div>
  )
}

function Row({
  profile,
  canEdit,
  onEdit,
  onDuplicate,
  onMakeDefault,
  onRetire,
  onRestore,
  onDelete,
}: {
  profile: ScoringProfile
  canEdit: boolean
  onEdit: () => void
  onDuplicate: () => void
  onMakeDefault?: () => void
  onRetire?: () => void
  onRestore?: () => void
  onDelete: () => void
}) {
  // Seeded sets are read only, including to an admin: the name is a claim about
  // a standard. Duplicating is how anything about them gets changed.
  const locked = profile.isSeeded

  return (
    <li
      className={[
        'flex flex-wrap items-center gap-x-3 gap-y-2 rounded-xl px-4 py-3',
        profile.isActive
          ? 'bg-card ring-1 ring-foreground/10'
          : 'border border-dashed border-foreground/20',
      ].join(' ')}
    >
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-semibold">{profile.name}</span>

          {profile.isDefault && (
            <span className="rounded-full bg-primary px-2 py-0.5 text-[11px] font-semibold text-primary-foreground">
              Default
            </span>
          )}

          {locked && (
            <span
              title="Read only. Duplicate it to edit."
              className="inline-flex items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-[11px] font-semibold text-muted-foreground"
            >
              <Lock className="size-3" />
              Standard
            </span>
          )}
        </div>

        {/* Just the table. What the tie and DQ rules are is a question you ask
            of one set at a time, and the answer is in the editor. */}
        <p className="mt-0.5 text-sm text-muted-foreground tabular-nums">
          {profile.config.placePoints.join(' / ')}
        </p>
      </div>

      <div className="flex shrink-0 flex-wrap items-center gap-0.5">
        {canEdit && onMakeDefault && !profile.isDefault && (
          <Button
            variant="ghost"
            size="sm"
            aria-label={`Make ${profile.name} the default`}
            onClick={onMakeDefault}
          >
            <Star />
            <span className="hidden sm:inline">Make default</span>
          </Button>
        )}

        <Button
          variant="ghost"
          size="sm"
          aria-label={`${canEdit && !locked ? 'Edit' : 'View'} ${profile.name}`}
          onClick={onEdit}
        >
          <Pencil />
          <span className="hidden sm:inline">{canEdit && !locked ? 'Edit' : 'View'}</span>
        </Button>

        {canEdit && (
          <Button
            variant="ghost"
            size="sm"
            aria-label={`Duplicate ${profile.name}`}
            onClick={onDuplicate}
          >
            <Copy />
            <span className="hidden sm:inline">Duplicate</span>
          </Button>
        )}

        {/* Neither the default nor the standard table can be retired: a church
            with no default has nothing to start a session with. */}
        {canEdit && onRetire && !profile.isDefault && (
          <Button
            variant="ghost"
            size="sm"
            aria-label={`Retire ${profile.name}`}
            onClick={onRetire}
          >
            <ArchiveX />
            <span className="hidden sm:inline">Retire</span>
          </Button>
        )}

        {canEdit && onRestore && (
          <Button
            variant="ghost"
            size="sm"
            aria-label={`Put ${profile.name} back`}
            onClick={onRestore}
          >
            <ArchiveRestore />
            <span className="hidden sm:inline">Put back</span>
          </Button>
        )}

        {/* Only a set no session was started from, never the default, and never
            the standard table. */}
        {canEdit && !locked && !profile.isDefault && profile.sessionCount === 0 && (
          <Button
            variant="ghost"
            size="sm"
            aria-label={`Delete ${profile.name}`}
            onClick={onDelete}
            className="text-destructive hover:bg-destructive/10 hover:text-destructive"
          >
            <Trash2 />
            <span className="hidden sm:inline">Delete</span>
          </Button>
        )}
      </div>
    </li>
  )
}

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="rounded-xl border border-dashed p-8 text-center">
      <p className="font-medium">{title}</p>
      <p className="mx-auto mt-1 max-w-md text-sm leading-relaxed text-muted-foreground">
        {detail}
      </p>
    </div>
  )
}
