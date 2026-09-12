# Scoring rules

This is the authoritative description of how a round is scored. The engine in
`apps/api/src/Awana.Scoring` implements exactly this, and the test suite works
straight down the worked examples at the bottom.

It is also meant to be printed and kept with the scorekeeper, so that a disputed
round can be settled by looking something up rather than by arguing.

## The points table

By default, four places are worth:

| Place | Points |
|---|---|
| 1st | 40 |
| 2nd | 30 |
| 3rd | 20 |
| 4th | 10 |

The table is configuration, not code. A church can change the values, use more
or fewer entries, and set what teams past the end of the table earn. The table
length and the number of teams are independent: a four entry table with six
teams is well defined, and the fifth and sixth teams earn the "beyond table"
value, which defaults to 0.

## How a round is scored

1. Every team is either **placed** at a finishing position, or marked **absent**.
2. Places are normalized so they run 1, 2, 3 with no gaps. Recording places as
   1, 2, 4 scores identically to 1, 2, 3.
3. Teams that finished level share a **place group**.
4. Each group consumes as many **slots** as it has members, starting from the
   next free slot.
5. The points for the slots a group consumed are totaled and split as the tie
   rule says. By default they are averaged.
6. The round **multiplier** is applied.
7. **Bonus points** are added afterwards, so they are never multiplied.
8. The result is rounded.

Absent teams consume no slots, earn no points, and do not shift anyone else.

## Ties

The default rule is **average the shared slots**.

Two teams tied for first consume slots 1 and 2. Those slots are worth 40 and 30,
totaling 70, so each team earns 35. The next team along takes slot 3 and earns
20, not 30. A tie never lets the team behind it gain from someone else's tie.

Three other tie rules are available as configuration:

| Rule | Behavior |
|---|---|
| `AverageSharedSlots` | Default. Every tied team gets the average of the slots consumed. |
| `HighestSlot` | Every tied team gets the best slot's value. Generous, and inflates the round total. |
| `LowestSlot` | Every tied team gets the worst slot's value. |
| `NoTiesAllowed` | The round is rejected and the scorekeeper must break the tie. |

### Why averaging is the default

Each round is a fixed pot. Slots 1 through 4 are worth 100 points between them,
and placement decides how that pot is divided. A tie means the teams could not be
separated, so the fair answer is to split what those positions were worth.

Awarding both tied teams the higher value invents points nobody earned, and it
makes a night with more close finishes worth more in total than a night without.
Season standings would then depend partly on how close the races happened to be,
which no team controls.

Averaging is also the only rule that can be checked by the kids asking about it.
"Places 1 and 2 are worth 40 plus 30, which is 70, split two ways, so 35 each"
is arithmetic anyone can follow. "You both get 40" has no reasoning to appeal to.

The objection that comes up is that 35 feels like a penalty for tying, since an
outright win pays 40. Two answers. `LowestSlot` is the punitive rule, where both
teams get 30, so averaging is the neutral middle. And a tie for first still pays
35, which beats the 30 that second place would have paid, so tying is never worse
than losing.

The rule stays configurable because other churches score differently, and because
each session freezes its scoring profile at the moment it starts. Changing the
default later cannot alter a result that has already been recorded.

## Disqualification

The default rule is **zero, but hold the slot**.

A disqualified team keeps the position it finished in and earns nothing. It does
not promote the teams behind it. Disqualifying the team that came first does not
turn the second place team into a winner.

### The case worth reading twice

A disqualified team **inside a tie group**.

The group still consumes all of its slots and the average is still taken over
all of its members. The disqualified member earns 0, and **its share is
forfeited rather than redistributed to the others**.

If the share were redistributed, the team tied with the disqualified team would
end up better off than if its partner had never been disqualified. A
disqualification would become a reward for whoever tied with them, and the rule
that a disqualification does not promote anyone would quietly stop being true
inside tie groups.

Three other disqualification rules are available:

| Rule | Behavior |
|---|---|
| `ZeroButHoldSlot` | Default. Keeps its slot, earns 0, promotes nobody. |
| `ZeroAndPromoteOthers` | Removed from the ordering, so everyone behind it moves up. |
| `DropToLast` | Placed last, and the teams behind it move up. |
| `CustomPenalty` | Earns a configured negative or zero value, keeping its slot. |

## Multipliers and bonuses

A round can carry a **multiplier**, for a final round or a tug of war worth
double. It applies to the placement points only.

A team can also be given **bonus points**, for a game that awards extras. Bonuses
are added after the multiplier, so a double round does not double a bonus. This
is deliberate: bonuses are usually a flat award for a specific achievement, and
multiplying them tends to surprise people.

The worked example from this church is the **bonus bucket**. After a relay, the
finisher could throw a beanbag at a bucket in the center circle, and landing it
earned the team 20 points. Any team could earn it, including the one that had
just finished last, so it is recorded per team on the round rather than tied to
a placement.

## Whole numbers

Scores are awarded as whole numbers by default.

This costs nothing on the official table. Every split it can produce is already
an integer: any two adjacent places average to 35, 25 or 15, any three to 30 or
20, and all four to 25. Showing decimals would only ever add ".00" to a number
on a projector. A test asserts this across every tie pattern, so the claim
cannot quietly stop being true.

A church using a table that does divide unevenly, such as 50 / 25 / 15 / 10, can
raise the precision in its scoring profile. The underlying column stores three
decimal places either way, so nothing is lost by changing the setting later.

## Worked examples

Four teams, default table of 40 / 30 / 20 / 10, multiplier 1, no bonuses. These
are the test cases.

| Situation | Red | Blue | Yellow | Green |
|---|---|---|---|---|
| No ties, in order | 40 | 30 | 20 | 10 |
| Red and Blue tie for 1st | 35 | 35 | 20 | 10 |
| Blue and Yellow tie for 2nd | 40 | 25 | 25 | 10 |
| Yellow and Green tie for 3rd | 40 | 30 | 15 | 15 |
| Red/Blue tie 1st, Yellow/Green tie 3rd | 35 | 35 | 15 | 15 |
| Red, Blue, Yellow tie for 1st | 30 | 30 | 30 | 10 |
| Blue, Yellow, Green tie for 2nd | 40 | 20 | 20 | 20 |
| All four tie | 25 | 25 | 25 | 25 |
| Red disqualified in 1st | 0 | 30 | 20 | 10 |
| Red and Blue tie 1st, Red disqualified | 0 | 35 | 20 | 10 |
| All four disqualified | 0 | 0 | 0 | 0 |
| Green absent, other three in order | 40 | 30 | 20 | absent |

Note the difference between rows five and ten. When Red is disqualified inside a
tie for first, Blue still earns 35 and not 70. Blue's score is unaffected by what
happened to Red.

### Rounding

Only reachable on a table that divides unevenly, which the official one does not.

With a table of 50 / 25 / 15 / 10, three teams tied for second consume slots 2,
3 and 4, worth 25 + 15 + 10 = 50. Split three ways that is 16.666..., recorded
as 17 at the default whole number precision, or 16.67 if the profile asks for
two decimal places.

### More or fewer teams than the table

Six teams against the four entry table: the fifth and sixth teams earn the
beyond table value, 0 by default.

Three teams against the four entry table: they earn 40, 30 and 20. The unused
fourth slot is simply never consumed.

## Invariants

These hold for every input and are enforced by tests:

- **Order independence.** Shuffling the order teams are listed in does not change
  anyone's points. Only their recorded places matter.
- **Conservation.** With no disqualifications, no bonuses and a multiplier of 1,
  the total points awarded equal the total value of the slots consumed.
- **Totality.** Every team in the input appears exactly once in the output, with
  no team invented and none dropped.

## What the scorekeeper sees

Before a round is saved, the console shows the points and the reasoning:

> **Red** 35.00, tied for 1st with Blue. Places 1 and 2 share 40 + 30 = 70, split
> 2 ways.
>
> **Yellow** 0.00, 4th place, disqualified. Slot retained, no team promoted.

The explanation is stored with the result, so a round can be explained weeks
later without recomputing it.
