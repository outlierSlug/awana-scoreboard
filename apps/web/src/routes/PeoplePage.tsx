import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ClipboardList, Eye, Flag, Info, ShieldCheck, UserPlus, UserX, type LucideIcon } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { ActivityList } from '@/components/people/ActivityList'
import { AddPersonDialog } from '@/components/people/AddPersonDialog'
import { PeopleHelpDialog } from '@/components/people/PeopleHelpDialog'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { useMe } from '@/lib/auth'
import { api, ApiError } from '@/lib/apiClient'
import { queryKeys } from '@/lib/queryClient'
import { ROLE_LABEL, ROLE_SUMMARY, ROLES_BY_ACCESS } from '@/lib/roles'
import { UserRole, type Person } from '@/lib/types'

const ROLE_ICON: Record<UserRole, LucideIcon> = {
  [UserRole.Admin]: ShieldCheck,
  [UserRole.GamesLeader]: Flag,
  [UserRole.Scorekeeper]: ClipboardList,
  [UserRole.Viewer]: Eye,
}

const PLURAL: Record<UserRole, string> = {
  [UserRole.Admin]: 'Admins',
  [UserRole.GamesLeader]: 'Games leaders',
  [UserRole.Scorekeeper]: 'Scorekeepers',
  [UserRole.Viewer]: 'Viewers',
}

/**
 * Who can sign in, what each of them may do, and what has been done.
 *
 * Grouped by role, so "who can run the night" is answered by looking at a
 * heading rather than by reading every row. Deactivated accounts sit at the
 * bottom rather than disappearing, because the question "why can't Sam sign in
 * any more" needs somewhere to find the answer.
 */
export function PeoplePage() {
  const queryClient = useQueryClient()
  const { me, can } = useMe()
  const isAdmin = can(UserRole.Admin)

  const [search, setSearch] = useSearchParams()
  const view = search.get('view') === 'activity' ? 'activity' : 'people'

  const [adding, setAdding] = useState(false)
  const [helping, setHelping] = useState(false)
  const [changing, setChanging] = useState<{ person: Person; role: UserRole } | null>(null)
  const [deactivating, setDeactivating] = useState<Person | null>(null)

  const people = useQuery<Person[]>({
    queryKey: queryKeys.people(),
    queryFn: ({ signal }) => api.people(signal),
    enabled: isAdmin,
  })

  // Every change here is also a new line of activity, so both are refreshed.
  const refresh = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.people() }),
      queryClient.invalidateQueries({ queryKey: queryKeys.activity() }),
    ])
  }

  const add = useMutation({
    mutationFn: ({ email, role }: { email: string; role: UserRole }) => api.addPerson(email, role),
    onSuccess: async () => {
      setAdding(false)
      await refresh()
    },
  })

  const setRole = useMutation({
    mutationFn: ({ person, role }: { person: Person; role: UserRole }) => api.setPersonRole(person.id, role),
    onSuccess: async () => {
      setChanging(null)
      await refresh()
    },
  })

  const deactivate = useMutation({
    mutationFn: (person: Person) => api.deactivatePerson(person.id),
    onSuccess: async () => {
      setDeactivating(null)
      await refresh()
    },
  })

  const reactivate = useMutation({
    mutationFn: (person: Person) => api.reactivatePerson(person.id),
    onSuccess: refresh,
  })

  // The API refuses all of this for anyone else. Saying so beats an empty page
  // or a list of permission errors.
  if (!isAdmin) {
    return (
      <div className="flex flex-col gap-6">
        <h1 className="text-2xl font-bold tracking-tight">People</h1>
        <div className="rounded-xl border border-dashed p-10 text-center">
          <p className="font-medium">Only admins can manage people.</p>
          <p className="mt-1 text-sm text-muted-foreground">
            Ask an admin if you need different access.
          </p>
        </div>
      </div>
    )
  }

  const active = people.data?.filter((p) => p.isActive) ?? []
  const inactive = people.data?.filter((p) => !p.isActive) ?? []

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-1.5">
          <h1 className="text-2xl font-bold tracking-tight">People</h1>

          <Button variant="ghost" size="icon-sm" aria-label="How roles work" onClick={() => setHelping(true)}>
            <Info />
          </Button>
        </div>

        <Button size="lg" onClick={() => setAdding(true)}>
          <UserPlus />
          Add person
        </Button>
      </div>

      <ToggleGroup
        type="single"
        value={view}
        aria-label="Show"
        className="h-10 self-start rounded-lg border border-border p-0.5"
        onValueChange={(value) => {
          if (!value) return
          // In the address, so a link to the activity log opens the activity log.
          setSearch(value === 'activity' ? { view: 'activity' } : {}, { replace: true })
        }}
      >
        {[
          { value: 'people', label: 'People' },
          { value: 'activity', label: 'Activity' },
        ].map((option) => (
          <ToggleGroupItem
            key={option.value}
            value={option.value}
            className="rounded-md px-4 text-sm font-semibold data-[state=on]:bg-primary data-[state=on]:text-primary-foreground"
          >
            {option.label}
          </ToggleGroupItem>
        ))}
      </ToggleGroup>

      <PeopleHelpDialog open={helping} onClose={() => setHelping(false)} />

      <AddPersonDialog
        open={adding}
        onClose={() => {
          setAdding(false)
          add.reset()
        }}
        busy={add.isPending}
        error={add.error}
        onAdd={(email, role) => add.mutate({ email, role })}
      />

      <ConfirmDialog
        open={changing !== null}
        title={changing ? `Make ${nameOf(changing.person)} ${article(ROLE_LABEL[changing.role])}?` : ''}
        confirmLabel="Change role"
        busy={setRole.isPending}
        onCancel={() => {
          setChanging(null)
          setRole.reset()
        }}
        onConfirm={() => changing && setRole.mutate(changing)}
      >
        {changing && (
          <>
            <p>
              From {ROLE_LABEL[changing.person.role]} to {ROLE_LABEL[changing.role]}.{' '}
              {ROLE_SUMMARY[changing.role]}
            </p>
            <p className="mt-2">It applies straight away, without them signing out.</p>
          </>
        )}
        <MutationError error={setRole.error} />
      </ConfirmDialog>

      <ConfirmDialog
        open={deactivating !== null}
        title={deactivating ? `Deactivate ${nameOf(deactivating)}?` : ''}
        confirmLabel="Deactivate"
        destructive
        busy={deactivate.isPending}
        onCancel={() => {
          setDeactivating(null)
          deactivate.reset()
        }}
        onConfirm={() => deactivating && deactivate.mutate(deactivating)}
      >
        <p>
          They are signed out on their next request and cannot sign back in. Everything they
          recorded stays as it is, and reactivating gives them back the {deactivating && ROLE_LABEL[deactivating.role].toLowerCase()} role.
        </p>
        <MutationError error={deactivate.error} />
      </ConfirmDialog>

      {view === 'activity' ? (
        <ActivityList />
      ) : (
        <>
          {people.isPending && <div className="h-40 animate-pulse rounded-xl bg-muted" />}

          {people.error && (
            <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
              Could not load people. {(people.error as Error).message}
            </p>
          )}

          {reactivate.error instanceof ApiError && (
            <p className="rounded-xl border border-destructive/30 bg-destructive/5 p-4 text-sm">
              {reactivate.error.message}
            </p>
          )}

          {ROLES_BY_ACCESS.map((role) => {
            const inRole = active.filter((p) => p.role === role)
            if (inRole.length === 0) return null

            return (
              <PeopleSection key={role} Icon={ROLE_ICON[role]} label={PLURAL[role]} count={inRole.length}>
                {inRole.map((person) => (
                  <PersonRow
                    key={person.id}
                    person={person}
                    isYou={person.id === me?.userId}
                    onRole={(next) => setChanging({ person, role: next })}
                    onDeactivate={() => setDeactivating(person)}
                  />
                ))}
              </PeopleSection>
            )
          })}

          {inactive.length > 0 && (
            <PeopleSection Icon={UserX} label="Deactivated" count={inactive.length}>
              {inactive.map((person) => (
                <PersonRow
                  key={person.id}
                  person={person}
                  isYou={false}
                  busy={reactivate.isPending}
                  onReactivate={() => reactivate.mutate(person)}
                />
              ))}
            </PeopleSection>
          )}
        </>
      )}
    </div>
  )
}

function PeopleSection({
  Icon,
  label,
  count,
  children,
}: {
  Icon: LucideIcon
  label: string
  count: number
  children: React.ReactNode
}) {
  return (
    <section>
      <h2 className="mb-2 flex items-center gap-2 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
        <Icon className="size-4" />
        {label}
        <span className="font-normal tabular-nums">{count}</span>
      </h2>
      <ul className="flex flex-col gap-2">{children}</ul>
    </section>
  )
}

function PersonRow({
  person,
  isYou,
  busy = false,
  onRole,
  onDeactivate,
  onReactivate,
}: {
  person: Person
  isYou: boolean
  busy?: boolean
  onRole?: (role: UserRole) => void
  onDeactivate?: () => void
  onReactivate?: () => void
}) {
  // A name only exists once Google has supplied one, so until then the address
  // is the name and there is nothing to show beneath it.
  const hasName = person.displayName !== person.email

  // Why a row cannot be edited, when it cannot, said on the row rather than as
  // a control that is simply greyed out.
  const locked = isYou ? 'This is you' : person.isConfigAdmin ? 'Set on the server' : null

  return (
    <li className="flex flex-col gap-3 rounded-xl bg-card p-4 ring-1 ring-foreground/10 sm:flex-row sm:items-center">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className="truncate font-semibold">{hasName ? person.displayName : person.email}</span>
          {isYou && <Badge>You</Badge>}
          {person.isConfigAdmin && <Badge>Server admin</Badge>}
          {!person.hasSignedIn && <Badge muted>Not signed in yet</Badge>}
        </div>

        <span className="mt-0.5 block truncate text-sm text-muted-foreground">
          {hasName && `${person.email} · `}
          {person.lastLoginAt
            ? `Last signed in ${new Date(person.lastLoginAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}`
            : 'Never signed in'}
          {!person.isActive && ` · was ${ROLE_LABEL[person.role].toLowerCase()}`}
        </span>
      </div>

      {person.isActive ? (
        locked ? (
          <span className="text-sm text-muted-foreground sm:text-right">
            {ROLE_LABEL[person.role]} · {locked}
          </span>
        ) : (
          <div className="flex items-center gap-2">
            <Select value={person.role} onValueChange={(value) => onRole?.(value as UserRole)}>
              <SelectTrigger aria-label={`Role for ${person.email}`} className="h-9 w-40 text-sm font-medium">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {ROLES_BY_ACCESS.map((role) => (
                  <SelectItem key={role} value={role}>
                    {ROLE_LABEL[role]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Button variant="ghost" size="sm" onClick={onDeactivate}>
              Deactivate
            </Button>
          </div>
        )
      ) : (
        <Button variant="outline" size="sm" disabled={busy} onClick={onReactivate}>
          Reactivate
        </Button>
      )}
    </li>
  )
}

function Badge({ children, muted = false }: { children: React.ReactNode; muted?: boolean }) {
  return (
    <span
      className={[
        'rounded-full px-2 py-0.5 text-xs font-medium',
        muted ? 'bg-muted text-muted-foreground' : 'bg-primary/10 text-foreground',
      ].join(' ')}
    >
      {children}
    </span>
  )
}

function MutationError({ error }: { error: unknown }) {
  if (!(error instanceof ApiError)) return null

  return (
    <p className="mt-3 rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-destructive">
      {error.message}
    </p>
  )
}

function nameOf(person: Person): string {
  return person.displayName !== person.email ? person.displayName : person.email
}

/** "a Scorekeeper", "an Admin". */
function article(label: string): string {
  return /^[AEIOU]/i.test(label) ? `an ${label}` : `a ${label}`
}
