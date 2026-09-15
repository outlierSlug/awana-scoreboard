import { HelpModal, Term, Topic } from '@/components/ui/help'

/**
 * What the game catalog does, kept off the catalog page.
 *
 * All of this used to sit as prose around the list, which made a screen whose
 * job is "find one game" read like a manual. It is genuinely worth saying once,
 * though: retiring and deleting look like the same action and are not, and
 * somebody meets this page for the first time roughly once a season.
 */
export function GameHelpDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <HelpModal open={open} onClose={onClose} title="Game catalog">
      <Topic title="What this list is">
        <p>A catalog of games the scorekeeper picks from when recording a round.</p>
      </Topic>

      <Topic title="The order">
        <p>
          Exactly the order a scorekeeper sees when picking a game, so put the ones run every week
          at the top. Drag a game by the handle on its left to move it.
        </p>
      </Topic>

      <Topic title="Editing, retiring, and deleting">
        <Term label="Editing">allows you to configure a game&rsquo;s name and rules.</Term>

        <Term label="Retiring">
          a game removes it from the scorekeeper&rsquo;s list of selectable games. It can be
          reinstated at any time. Any rounds from past sessions for this game are preserved.
        </Term>

        <Term label="Deleting">
          a game removes it from the catalog permanently. This cannot be undone. A game with
          rounds against it cannot be deleted.
        </Term>
      </Topic>
    </HelpModal>
  )
}
