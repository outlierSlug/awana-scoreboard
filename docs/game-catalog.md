# Game catalog

The games actually played, taken from the 2023-2024 season archive. This is what
the seeder ships so that real names are in the dropdown on night one, rather
than placeholder data someone has to replace in the gym.

Every game uses the same 40 / 30 / 20 / 10 points table. What differs between
them is how finishing order is *determined*, not what it is worth. A relay is
decided by who crosses first; Beanbag Curling is decided by a curling style
distance score. The scorekeeper enters the resulting order either way, so the
engine does not need to know the difference.

## Core games

The four iconic Awana games, played most weeks.

| Game | Notes |
|---|---|
| Baton Relay | |
| Three-Legged Race | |
| Scooter Relay | |
| Tug-of-War | Often a closing game, and a natural candidate for a round multiplier |

## Occasional games

Played less often, usually alongside a core game.

| Game | Origin |
|---|---|
| Noodle Relay | Traditional |
| Cup Stacking | Traditional |
| Egg Relay | Traditional |
| Balance Relay | Traditional |
| Potato Sack Relay | Traditional |
| Beanbag Curling | Created or adapted locally |
| Prize Pool | Created or adapted locally |
| Number Calling | Created or adapted locally |
| Color Cube | Created or adapted locally |
| Steal | Created or adapted locally. T&T only. |

All games are available to both divisions except Steal, which was only ever run
with T&T.

## Rounds versus games

A game is a game *type*, and one game is usually run several times in an
evening. On 6 October 2023 the Sparks played a single game but scored 600 points
across the four teams, which is six rounds of 100.

The schema reflects this: a `Round` references a `Game`, and many rounds can
reference the same one. The scoreboard counts rounds. A future rotation view,
which answers "what have we not played for a while", counts distinct games.

## The bonus bucket

A locally invented bonus, and the reason session totals are not always a
multiple of 100.

After a relay finished, the finisher could take a beanbag and throw it at a
bucket in the middle of the center circle. Landing it earned the team **20
points**. Any team could earn it, including one that had just come last.

This is a per team, per round bonus of a fixed amount. The engine models it
directly as `TeamEntry.Bonus`, and bonuses are added after any round multiplier,
so a double round does not turn a 20 point bucket into 40.

## Teams

Four teams per division: Red, Blue, Green, Yellow. Rounds were never run with
any other number.

Sparks Red and T&T Red are different teams with separate histories, so they are
separate rows rather than a shared color lookup.
