# Game catalog

The games actually played, taken from the 2023-2024 season archive. This is what
the seeder ships so that real names are in the dropdown on night one, rather
than placeholder data someone has to replace in the gym.

**The seed is a starting point, not the list.** Since 2026-09-14 the catalog is
edited in the app at `/app/games` by a games leader or an admin: add a game,
rename one, write down its rules, drag it to where it belongs in the picker,
retire what is no longer played. A church that plays none of these can retire
the lot and add its own. This file records where the seed came from; the
database is what the club actually plays.

The page is a list of names in picker order, each row carrying its own Edit,
Retire and Delete. Editing opens a dialog with two fields, a name and its rules,
and nothing else.

**What the catalog deliberately does not show.** How often a game has been
played, when it was last run, what has not come up for a while: none of it
appears here, and none of it should be added. That is week to week tracking,
which is the job the old planning Google Sheet actually did, and it belongs in
its own view that READS this catalog. This page answers one question, which is
what a game is and what it is called.

The API does return a round count per game, but as a gate rather than a
statistic: zero is what makes a game deletable. It is never rendered.

**One hand-arranged list, no tiers.** There used to be an `IsCore` flag marking
the four iconic games so they sorted above the rest. It was dropped on
2026-09-14: it turned out to be a clumsier way of saying "put these four first",
and a club that plays five every week, or swaps one for a season, had to argue
with a category instead of dragging a row. `Game.SortOrder` is now the whole
answer, and the seed simply starts with the four iconic games at the top.

**A game that has been played can never be deleted.** Rounds reference it, and
the board has to keep reading correctly in a year. Retiring is the answer: out
of the picker, past untouched. Delete is only offered for a game with no
standing rounds, and only to an admin.

**Every game is offered to every division**, and `Game.DivisionId` was dropped
on 2026-09-14. It existed for exactly one row: Steal, which had only ever been
run with T&T. That turned out to be a fact about one evening rather than a rule
worth enforcing, and it cost a field in every game's dialog, a validation rule
and a conflict error to keep. The scorekeeper picks whatever is actually being
played, so one shared list serves a Sparks session and a T&T session equally.

Renaming is deliberately retroactive. A rename corrects what the game has always
been called, so every round already played on it picks up the new name. If a
genuinely different game is meant, add one.

Each seeded game carries a hidden `seed_key`. The seeder matches on that rather
than on the name, so renaming a seeded game does not cause the next boot to
create a duplicate beside it. Rows that predate the column are adopted by name
once, and that adoption is the only moment the seeder fills in a blank note.

Every game uses the same 40 / 30 / 20 / 10 points table. What differs between
them is how finishing order is *determined*, not what it is worth. A relay is
decided by who crosses first; Beanbag Curling is decided by a curling style
distance score. The scorekeeper enters the resulting order either way, so the
engine does not need to know the difference.

## Core games

The four iconic Awana games, played most weeks. They are seeded first, which is
a starting order rather than a category.

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
| Steal | Created or adapted locally. Run once, with T&T. |

Every game is available to every division.

The seed writes short rules on each of the standard games describing how the
finishing order is decided. The five invented or adapted here are seeded blank
on purpose: only the leaders who run them know how they actually go, and a
guessed description is worse than an empty one because it reads as the record.
The catalog marks those "no rules yet" and invites somebody to fill them in.

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

This is a per team, per round bonus. The engine models it directly as
`TeamEntry.Bonus`, and bonuses are added after any round multiplier, so a double
round does not turn a 20 point bucket into 40.

**The amount is entered per round, not fixed.** The archive suggests the bonus
was sometimes 10 and sometimes 20, and a future game will award something else
again. So the scorekeeper console gets a number field defaulting to 20, not a
checkbox that hardcodes it.

## Teams

Four teams per division: Red, Blue, Green, Yellow. Rounds were never run with
any other number.

Sparks Red and T&T Red are different teams with separate histories, so they are
separate rows rather than a shared color lookup.
