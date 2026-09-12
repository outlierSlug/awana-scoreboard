# How a games night actually runs

The minute by minute of a live session, written from the scorekeeper's side. This
is the specification the console is designed against, and the reason particular
screens exist.

Context: games run for about 30 minutes inside a 7:15 to 8:45pm evening, and a
session is 4 to 8 rounds. Sparks and T&T usually play one after the other, but
the system must support both running at once with each display pinned to one.

## Before the first race

**Sign in.** The scorekeeper opens the console on a phone or a laptop. Both are
first class. A volunteer at a table with a laptop is as likely as one standing
with a phone.

**Open tonight's session.** Either it was created earlier in the week or it gets
created now: pick the division, confirm the date. The four teams come from the
division, so there is nothing to configure.

**Optional headcounts.** One number per team. Skippable, and a session with none
recorded is completely valid. Never a name.

**Start the session.** This is the moment the scoring profile is frozen onto the
session. From here the points table, tie rule and DQ rule cannot change for this
night, so an admin editing the profile in December cannot silently rewrite
October.

**Put the board up.** Someone opens the board URL on the TV, appends the TV flag
for fullscreen, and the screen stays awake on its own. Before the first round it
reads "starting soon" rather than an empty table.

## Each round, four to eight times

1. **The games leader announces the game.** The scorekeeper picks it from the
   catalog. It stays selected for the next round, because the same game is
   usually run several times in a row.
2. **The kids race.**
3. **The scorekeeper taps team colors in finish order**, as teams cross. Each tap
   appends to a numbered list. This is the only interaction during the race
   itself, and it has to survive being done quickly and badly.
4. **Corrections, after the fact, never as a mode.** This is the central design
   rule. A scorekeeper realizes something *after* watching the finish, so every
   correction is applied to a row that already exists:
   - Two teams finished level: tap "tie with above" on the lower row.
   - A team is disqualified, for a false start or a dropped baton: tap the DQ
     badge on its row.
   - A team did not run: mark it not playing.
   - Wrong order: drag a row, or undo.
5. **Bonus points, if any.** The bonus bucket is the common case. A number field
   per team, defaulting to 20, because the amount has varied historically.
6. **Read the preview.** The panel shows what each team will score and why, in
   words, before anything is saved. "Red and Blue tied for 1st. Places 1 and 2
   share 40 plus 30 equals 70, split 2 ways, 35 each."
7. **Confirm.** A spinner until the server answers. Never optimistic: on a
   projected screen, briefly slow beats briefly wrong. The board and every other
   connected device update the moment the server accepts it.

## When something goes wrong

Disputes surface within a minute or two and get settled on the spot, so the
console needs good controls rather than a separate correction mode.

| Situation | Control |
|---|---|
| Tapped the wrong team, not yet confirmed | Undo, repeatedly, up to about 20 steps |
| Confirmed a round that was wrong | Void the round. It is never deleted, and totals recompute |
| A round from earlier in the night is wrong | Edit it. Totals recompute for every team, including teams absent from that round |
| A team deserves points outside the games | Manual adjustment with a written reason |
| Session marked finished too early | Reopen it. This will be needed in the gym |

Every one of these recomputes standings from the stored round results rather than
adjusting a running total, so the numbers cannot drift.

## After the last race

**Finish the session.** The board switches to final standings and stops accepting
rounds. A round posted to a finished session is rejected rather than silently
accepted.

## What the room sees

The board is the product. It is on a TV or projector, viewed from up to about 30
feet, by children who will absolutely notice if it is wrong or stale.

- Four team rows filling the screen, never scrolling.
- Points large enough to read from the back of the gym.
- Team name always shown, never color alone, because roughly 8 percent of boys
  are red-green colorblind and Red and Green are two of the four teams.
- Rows reorder with a visible animation when the standings change. This is the
  payoff, and the reason kids stop asking the scorekeeper what the score is.
- A connection indicator, because silent staleness is worse than a visible error.

## The failure that must not happen

The phone dies, or the wifi drops, mid session.

The design answer is that the scorekeeper console and the board join the same
live channel, so a second device can be opened at any time and shows identical
state with no handover step. Beyond that, a paper clipboard stays in the room on
launch night.
