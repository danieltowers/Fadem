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

## Noted from ticket 03

[Ticket 03](03-what-position-does-set-from-here-capture.md) settled that **playhead** and
**decode position** are distinct types, so a mix-up does not compile — but scoped that
deliberately to the position quantities and left the general question here.

v2's *other* headline defect was frames-versus-samples confusion (`FramesToSamplesProvider`),
which is the same disease: two frame counts in different units sharing a type. Whether the
seam should carry typed units generally — frames, samples, device bytes, engine frames — is
this ticket's call.

Ticket 03 also grew the seam: `IAudioOutput` gains a member reporting frames actually emitted
by the device, because as written it carries no position or latency fact at all.

## Noted from ticket 05

Two facts that reframe this ticket's premise.

**The shell is thin already.** 1033 lines across 9 files, decomposed by concern, with the
composition root and the NAudio binding already separated. v2's spec asked for a thin shell and
*got one* — and every defect the user hit still lived in it. So thinness is not the property
this ticket is really after. The property is **reachability by a test**: there is no test project
referencing `Faderio.App`, so none of those 1033 lines was ever executed by the suite.

**The seam is about to carry more.** Ticket 03 adds a frames-emitted member to `IAudioOutput`;
[ticket 11](11-render-a-boundary-to-a-file.md) adds a file-writing implementation alongside
`NAudioAudioOutput` and `FakeAudioOutput`; ticket 05 makes output mode selectable at launch
rather than through a dialog. Three implementations and a launch-time selection is a real seam
with real pressure on it, which is a better test of this ticket's answer than one production
binding was.

## Noted from ticket 06

Two things ticket 06 hands to this ticket, both "verification by construction":

- **The seek bar's `Maximum` is the track length in milliseconds.** v2 never set `Minimum`,
  `Maximum` or `TickFrequency`, so WinForms' defaults gave 11 positions — 24-second seek
  granularity on a four-minute track, hidden by `TickStyle.None`. With `Maximum` in ms there is
  no scaling in either direction and the defect cannot be written. Which length it uses is
  [ticket 10](10-which-length-is-a-track.md)'s call.
- **Capture is an engine operation** — `engine.Capture()` returns a typed point — so the shell
  holds no position logic at all. That, plus ticket 03, empties the shell of everything but
  parse/format, row → adjacency, error summaries and store recovery. Whatever presenter this
  ticket designs holds exactly that list and nothing else.

Ticket 06 also fixed the shell's test reachability: a second test project on `net8.0-windows`
with `InternalsVisibleTo`, instantiating the real `MainForm` headless on an STA thread. The
seam this ticket designs should assume that test exists and can poke any control.
