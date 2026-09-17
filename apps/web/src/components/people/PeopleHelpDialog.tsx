import { Check } from 'lucide-react'
import { HelpModal, Term, Topic } from '@/components/ui/help'
import { CAPABILITIES, ROLE_LABEL, ROLES_BY_ACCESS } from '@/lib/roles'

const ROLE_RANK = Object.fromEntries(ROLES_BY_ACCESS.map((role, i) => [role, ROLES_BY_ACCESS.length - i]))

/**
 * What each role can do, and what the page's refusals are protecting.
 *
 * The table is built from the same list the rest of the app reads, so it
 * cannot claim a permission the API does not grant.
 */
export function PeopleHelpDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const columns = [...ROLES_BY_ACCESS].reverse()

  return (
    <HelpModal open={open} onClose={onClose} title="People and roles">
      <Topic title="What each role can do">
        <p>Every role can do everything the roles to its left can, and more.</p>

        <div className="-mx-1 overflow-x-auto">
          <table className="w-full min-w-[30rem] border-collapse text-left text-sm">
            <thead>
              <tr className="border-b">
                <th className="py-2 pr-2 pl-1 font-medium" />
                {columns.map((role) => (
                  <th key={role} className="px-2 py-2 text-center font-semibold text-foreground">
                    {ROLE_LABEL[role]}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {CAPABILITIES.map(({ label, minimum }) => (
                <tr key={label} className="border-b last:border-b-0">
                  <td className="py-2 pr-2 pl-1">{label}</td>
                  {columns.map((role) => (
                    <td key={role} className="px-2 py-2 text-center">
                      {ROLE_RANK[role] >= ROLE_RANK[minimum] ? (
                        <Check aria-label="Yes" className="mx-auto size-4 text-foreground" />
                      ) : (
                        <span className="sr-only">No</span>
                      )}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <p>
          The public scoreboard needs no account at all, so most people watching never need to be
          added here.
        </p>
      </Topic>

      <Topic title="Adding someone">
        <p>
          Use the Google account they will sign in with. Their name appears once they sign in for
          the first time; until then the list shows their email address.
        </p>
      </Topic>

      <Topic title="Changing access">
        <Term label="Role changes">apply straight away, without them signing out.</Term>
        <Term label="Deactivating">
          signs them out and stops them signing back in. Everything they recorded stays exactly as
          it was, and reactivating gives them back the role they had.
        </Term>
        <Term label="Nobody is deleted,">
          because rounds and the activity log keep saying who did what after someone stops
          volunteering.
        </Term>
      </Topic>

      <Topic title="What the page will not do">
        <Term label="Change your own access.">Another admin has to, so nobody locks themselves out by accident.</Term>
        <Term label="Remove the last admin.">Make someone else an admin first.</Term>
        <Term label="Edit a server admin.">
          Admins marked as set by the server come from the hosting configuration and would be put
          back on the next restart. They are the way back in if every other admin is removed.
        </Term>
      </Topic>
    </HelpModal>
  )
}
