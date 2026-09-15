import { HelpModal, Steps, Term, Topic } from '@/components/ui/help'

/**
 * What a session is, and how a night is actually run.
 *
 * Weighted towards the walkthrough rather than the state machine. Somebody
 * opening this is usually standing in a gym about to record a round, not
 * wondering what "setup" means in the abstract.
 */
export function SessionsHelpDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <HelpModal open={open} onClose={onClose} title="About sessions">
      <Topic title="What a session is">
        <p>
          A session is the games time for either Sparks or T&amp;T. Each session gets its own
          scorekeeping console and scoreboard.
        </p>
      </Topic>

      <Topic title="Running a night">
        <Steps>
          <li>
            <strong className="font-semibold text-foreground">Create or open</strong> tonight&rsquo;s session and press{' '}
            <strong className="font-semibold text-foreground">Start session</strong>. The session
            will become live and the scoreboard will become public.
          </li>
          <li>
            <strong className="font-semibold text-foreground">Open board</strong> puts the
            scoreboard on the screen or TV. Add{' '}
            <code className="rounded bg-muted px-1 py-0.5 text-xs">?tv=1</code> to the URL
            for a full screen with no menus around it.
          </li>
          <li>
            <strong className="font-semibold text-foreground">Teams</strong> is where you can 
            take attendance (headcounts), and where adjustments (bonuses, penalties) are recorded. Both are optional.
          </li>
          <li>
            <strong className="font-semibold text-foreground">Pick the game</strong>, and raise the multiplier if this round is worth more than the others.
          </li>
          <li>
            <strong className="font-semibold text-foreground">Tap the teams</strong> in the order they finished.{' '}
            <strong className="font-semibold text-foreground">Tie</strong> puts the next taps on
            the same place (tap the Tie button again to end the tie), <strong className="font-semibold text-foreground">Undo</strong> takes
            back the last action, and{' '}
            <strong className="font-semibold text-foreground">Reset</strong> clears the round.
          </li>
          <li>
            Check the points the current round is about to award, then click {' '}
            <strong className="font-semibold text-foreground">Confirm round</strong>. Nothing is
            saved and nothing reaches the scoreboard until this happens.
          </li>
          <li>
            Click <strong className="font-semibold text-foreground">Finish</strong> when the session is over.
            The scoreboard will continue to show the final scores, but no more rounds can be added.
          </li>
        </Steps>
      </Topic>

      <Topic title="Fixing a mistake">
        <Term label="Before confirming a round:">
          Undo takes back the last team tapped, and Reset clears the whole round.
        </Term>

        <Term label="After confirming a round:">
          Every round in the list can be edited or cleared. Editing is done in-place
          and scores are automatically recomputed; clearing removes the round from being scored.
        </Term>
      </Topic>

      <Topic title="Setup, live, finished">
        <p>
          A session made in advance sits in setup, where heads can be counted and the scoring
          rules can still be changed, but no round can be recorded. Starting moves it to live.
          Finishing closes it, and an admin can reopen a finished session if necessary.
        </p>
      </Topic>
    </HelpModal>
  )
}
