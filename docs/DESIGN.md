# Tower of Hanoi: design

Store title: **Tower of Hanoi**. Package `com.renegadegames.towerofhanoi`.

## The game
Move the whole tower from the left peg to the right one, one ring at a time, never putting a ring
on a smaller one. The Rings button picks 3 to 8 rings (best 7 to 255 moves:
2ⁿ − 1). Stars: three
for the fewest moves, two within half as many again, otherwise one.

## Feel
- Thick rings in a rainbow by size, finish from the Style menu (wood by default); pegs and base in
  the board material.
- Tap a peg to lift its top ring, tap another to drop it there with a clack (tap the same peg to
  put it back). A move that isn't allowed shakes the ring and says why.
- **Signature effect, the finale:** the finished tower lights up ring by ring from the bottom,
  each with a note a step higher, then a fountain of sparks, and (if you solved it yourself) the
  tower catches fire.
- **The formula** under the top buttons: "Fewest moves: 2ⁿ − 1 = 31". (A strip of bars, one per
  move of the perfect solution, was tried and removed: it gave the solution away.)
- Drag sideways to spin the board.

## Ads
Between-games interstitial every 5th game. **Hint (ad)** lights the best next move from any
position (the peg to take from, then the peg to put it on). **Solve (ads)** under it
finishes the puzzle for you after 1 ad (up to 4 rings), 2 (up to 6) or 3 (7 and 8), back to back;
no stars or fire for an assisted solve.

## Later
More pegs (Reve's puzzle), a timed mode, a free Undo.
