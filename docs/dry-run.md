# Dry run

A rehearsal of a whole games night against production, on the devices it will
actually run on. Run it before the first real night and again close to it.

The point is not to confirm the buttons work. That is covered by tests. The point
is to find the things only a room finds: a board nobody can read from the back, a
phone that locks itself mid-round, wifi that drops exactly once.

Budget an hour. Write down what went wrong rather than fixing it in the moment.

## Before

- The **console** on the phone or tablet that will be used on the night, not a
  desktop browser resized. Touch targets and one-handed reach are the thing being
  tested.
- The **board** on the actual TV or projector, at `?tv=1`, at the distance the
  back row will sit.
- Both signed in and charged. The console device should be at the battery level
  it will realistically be at by 8pm.
- A **clipboard and pen**. Not decoration: it is the fallback, and a rehearsal
  where it stays in the bag has not rehearsed the fallback.

Open the board with `?tv=1` for a full screen with no menus. The screen is kept
awake by a wake lock, but confirm the device's own sleep setting does not
override it.

## The run

Record a real night's worth, not two token rounds. Eight or ten.

1. Create tonight's session and **Start** it. The board should go from "nothing
   is live" to the four teams at zero without being reloaded.
2. Count heads in **Teams** and enter them.
3. Record rounds, mixing in the awkward ones deliberately:
   - a clean finish
   - a **tie** for first, and a tie for last
   - a **disqualification**
   - a round at **x2**
   - a round where you tap a team in the wrong order and use **Undo**
   - a round you **Reset** halfway through
4. After a few rounds, **edit** an earlier one and confirm the board's totals
   move to match.
5. **Clear** a round and confirm the same.
6. Add a **points adjustment** with a reason.
7. **Finish** the session. The board should say so and stop saying Live.

Watch the board, not the console, as each round is confirmed. What is being
measured is how long the room waits and whether the change is legible from the
back.

## Break it on purpose

These are the failures that actually happen, and each one has a defined right
answer.

| Do this | Expected |
|---|---|
| Turn off wifi on the **board** device for 30s, then back on | Scores stay on screen, status goes to Reconnecting, then recovers on its own with no reload |
| Turn off wifi on the **console** mid-round, then back on | The round in progress is not lost; confirming after reconnect works |
| Lock the console phone, wait a minute, unlock | Still signed in, still on the same session |
| Reload the board mid-session | Comes back with the current scores |
| Confirm a round twice (double tap) | One round recorded, not two |

The first row is the one that matters most, because it is the one a gym produces
without being asked.

## Cleanup

A session can be deleted once nothing is standing on it: clear every round and
every adjustment, then delete it. Cleared rounds do not block the delete, and the
audit log keeps the record either way.

Leave the production database as you found it, or keep the session deliberately
and know why.

## Write down

- Anything that needed a second attempt.
- Anything you had to think about rather than reach for.
- How long a round took, start to confirmed, once you had the rhythm.
- Whether the back row could read the board.

Those four are worth more than a list of bugs. A round that takes fifteen seconds
is a problem even when nothing failed.
