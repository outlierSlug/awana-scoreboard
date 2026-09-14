import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'

/**
 * What the catalog page does, kept off the catalog page.
 *
 * All of this used to sit as prose around the list, which made a screen whose
 * job is "find one game" read like a manual. It is genuinely worth saying once,
 * though: retiring and deleting look like the same action and are not, and
 * somebody meets this page for the first time roughly once a season.
 */
export function GameHelpDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Modal open={open} onClose={onClose} className="w-[min(34rem,calc(100%-2rem))]">
      <div className="flex flex-col gap-5 p-5">
        <h2 className="text-lg font-bold tracking-tight">About the game catalog</h2>

        <Topic title="What this list is">
          Every game the club plays, and what a scorekeeper picks from when recording a round.
          Open a game to read or change anything about it. Games carry no scoring rules: a first
          place is worth the same whichever game it was, so this is the list and the rules of
          play, not the points.
        </Topic>

        <Topic title="The order">
          Exactly the order a scorekeeper sees when picking a game, so put the ones run every
          Friday at the top. Drag a game by the handle on the left and drop it where you want it.
        </Topic>

        <Topic title="Rules">
          The only place it is written down how a game actually goes. Half of these were invented
          here and are carried from one year to the next by whoever ran them last, so this is what
          a leader taking over reads.
        </Topic>

        <Topic title="Retiring, and deleting">
          <strong className="font-semibold text-foreground">Retiring</strong> is how you stop
          playing a game. It leaves the scorekeeper&rsquo;s list, so no new rounds can be recorded
          on it, and every round ever played on it is kept and still reads correctly. You can put
          it back whenever you like.
          <br />
          <br />
          <strong className="font-semibold text-foreground">Deleting</strong> is for a game added
          by mistake. It is gone for good, only an admin can do it, and it is offered only while
          nothing has been played on the game. A game with rounds against it cannot be deleted at
          all, which is deliberate: the board has to keep reading correctly in a year.
        </Topic>

        <Topic title="Renaming">
          Retroactive, on purpose. A rename corrects what the game has always been called, so
          every round already played on it picks up the new name. If a genuinely different game is
          meant, add one instead.
        </Topic>

        <Button variant="outline" size="lg" onClick={onClose}>
          Close
        </Button>
      </div>
    </Modal>
  )
}

function Topic({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-1 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
        {title}
      </h3>
      <p className="text-sm leading-relaxed text-muted-foreground">{children}</p>
    </section>
  )
}
