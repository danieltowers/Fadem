# What is the seam between the shell and the engine?

Type: grilling
Status: open
Blocked by: —

## Question

v2's spec required a thin shell "so that nothing important lives there". It got one anyway,
and every defect the user experienced lived in it. The mechanism is worth stating precisely,
because the fix is architectural rather than a matter of care:

- The shell was handed the engine's live `PlaybackQueue` and mutated it directly. The add-files
  path calls `_engine.Queue.AddRange` (`src/Faderio.App/MainForm.Queue.cs:62`), bypassing
  `SetQueue`, which is the only place that records `_originalOrder`. Turning shuffle on and
  then off then restores an empty list and **the queue vanishes**.
- `MainForm.JumpTo` (`src/Faderio.App/MainForm.Display.cs:181-191`) hand-rolls
  stop-then-set-current-then-play instead of asking the engine to play a track.

Both are the same failure: the shell was given a mutable internal and had to know engine
invariants to use it correctly. It didn't.

**What does the shell get instead?** To settle:

1. Does the shell get a command surface, where the engine owns all queue state and exposes
   intent-shaped operations (`AddTracks`, `PlayTrackAt`, `SetShuffle`), or does it keep
   direct access to a queue object that is made safe?
2. Is the live queue collection reachable from the shell at all? Handing out a read-only
   projection for display, and refusing all mutation except through commands, makes this
   class of bug unrepresentable rather than merely discouraged.
3. How does the shell learn that state changed, so the ListView cannot drift out of sync with
   playback the way it did when shuffle was enabled?
4. What is the rule that tells a future session whether a piece of logic belongs in the shell
   or the engine, stated concretely enough to apply without judgement?

Consult the `codebase-design` skill: this is a deep-module question, and the engine's
interface is the thing being designed.

## Answer

_(pending)_
