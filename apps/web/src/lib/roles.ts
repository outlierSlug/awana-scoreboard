import { UserRole } from '@/lib/types'

/**
 * What each role is called out loud, and what it is for.
 *
 * One place, because three screens describe roles: the account menu, the form
 * that grants one, and the help that explains them. Written separately they
 * would drift, and a volunteer told "scorekeeper" in one place and something
 * slightly different in another has no way of knowing which is true.
 *
 * The API sends the enum name, which is right for it to send and wrong to put
 * in front of anybody: nobody outside this repository calls themselves a
 * GamesLeader.
 */
export const ROLE_LABEL: Record<UserRole, string> = {
  [UserRole.Viewer]: 'Viewer',
  [UserRole.Scorekeeper]: 'Scorekeeper',
  [UserRole.GamesLeader]: 'Games leader',
  [UserRole.Admin]: 'Admin',
}

/** Most access first, which is the order the People page lists them in. */
export const ROLES_BY_ACCESS: UserRole[] = [
  UserRole.Admin,
  UserRole.GamesLeader,
  UserRole.Scorekeeper,
  UserRole.Viewer,
]

/** One line each, for choosing between them. */
export const ROLE_SUMMARY: Record<UserRole, string> = {
  [UserRole.Admin]: 'Everything, including managing people and the scoring rules.',
  [UserRole.GamesLeader]: 'Runs the night: creates, starts and finishes sessions, and edits the games.',
  [UserRole.Scorekeeper]: 'Records rounds, headcounts and point adjustments during a session.',
  [UserRole.Viewer]: 'Can sign in and look, but cannot change anything.',
}

/**
 * The capability table, straight from the API's policies.
 *
 * `minimum` is the least role that can do it; every role above it can too. If
 * an endpoint's policy changes, this is the line to change with it.
 */
export const CAPABILITIES: { label: string; minimum: UserRole }[] = [
  { label: 'Sign in and see sessions and games', minimum: UserRole.Viewer },
  { label: 'Record, edit and clear rounds', minimum: UserRole.Scorekeeper },
  { label: 'Headcounts and point adjustments', minimum: UserRole.Scorekeeper },
  { label: 'View scoring rules', minimum: UserRole.Scorekeeper },
  { label: 'Create, start and finish sessions', minimum: UserRole.GamesLeader },
  { label: 'Add, edit, reorder and retire games', minimum: UserRole.GamesLeader },
  { label: 'Create and edit scoring rules', minimum: UserRole.Admin },
  { label: 'Reopen a finished session', minimum: UserRole.Admin },
  { label: 'Delete an unused game or an empty session', minimum: UserRole.Admin },
  { label: 'Manage people and review activity', minimum: UserRole.Admin },
]

export function roleLabel(role: string | null | undefined): string | null {
  if (!role) return null
  return ROLE_LABEL[role as UserRole] ?? role
}
