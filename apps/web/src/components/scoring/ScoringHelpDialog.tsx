import { HelpModal, Term, Topic } from '@/components/ui/help'

/**
 * How scoring rules work, kept off the scoring page.
 *
 * Most of this needs saying exactly once, to somebody meeting the screen for
 * the first time. Leaving it as prose around the list made a page with three
 * rows on it read like a manual.
 */
export function ScoringHelpDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <HelpModal open={open} onClose={onClose} title="Scoring">
      <Topic title="What this is">
        <p>
          The list of scoring rulesets available to the scorekeeper. Each ruleset delineates how
          many points each placement is worth, and how ties and disqualifications are handled. The
          chosen ruleset applies to every game in a session.
        </p>
      </Topic>

      <Topic title="Editing">
        <p>
          A session takes its own copy of the rules the moment it starts. Editing a set later
          never changes a night already played, so there is nothing to be careful about here and
          no need to make a new set just to correct a typo in an old one.
        </p>

        <p>
          Official AWANA is the standard table and cannot be changed. Duplicate it to make a set
          you can edit.
        </p>
      </Topic>

      <Topic title="Default, retiring, and deleting">
        <Term label="The default">
          ruleset is what a new session gets when nobody chooses. It can be neither retired nor
          deleted.
        </Term>

        <Term label="Retiring">
          a ruleset removes it from a session&rsquo;s selectable scoring options. It can be
          reinstated at any time.
        </Term>

        <Term label="Deleting">
          a ruleset is permanent, and can only be done to a set no session was ever run on.
        </Term>
      </Topic>

      <Topic title="Tie handling">
        <p>
          By default, tied teams share the average of the places they used up. Two teams tied for
          first consume both 1st and 2nd place, so they each score (40 + 30) / 2 = 35. The next
          team is then considered to have finished in 3rd place and earns 20, not 30.
        </p>
      </Topic>
    </HelpModal>
  )
}
