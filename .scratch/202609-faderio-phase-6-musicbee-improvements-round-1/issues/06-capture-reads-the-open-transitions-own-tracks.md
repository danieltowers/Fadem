# 06: Capture reads the open transition's own tracks

Status: resolved
Blocked by: none

Phase 6, improvements round 1, both hosts (the editor panel is shared). Raised 2026-10-02: "the
position of the playhead that is being captured isn't necessarily from the track we are
working with. e.g. track a is playing, track b is being edited, pressing capture from track b
will use track a's timeline".

## The cause

`PlaybackEngine.Capture(side, fromEnd)` takes no adjacency. Outside a preview it reads the
attended voice, whatever track that is (ADR 0003: "Outside a preview both sides read the
attended voice"). With A playing and the editor open on B → C, **Capture** beside the fade-out
point writes A's playhead into B's point, and since issue 05 that is saved at once. Only a
preview captures from the right tracks, because it plays the open adjacency's own voices.

The buttons are live whenever anything is heard
(`view.Attended is not null || view.Preview is not null` in the editor's `Refresh`), so nothing
warns that the press will read another track.

## What to build

Each capture reads the open transition's own track: the fade-out point its outgoing track, the
fade-in point its incoming track. A Capture button is live only while that track is heard, its
tooltip names the track, and while it is greyed a line under the field says what to play.

## Implementation decisions

- **The engine call takes the adjacency:** `Capture(adjacency, side, fromEnd)`. The track
  whose playhead counts is `adjacency.Outgoing` for `CaptureSide.FadeOut` and
  `adjacency.Incoming` for `FadeIn`. The playhead comes from the voice playing that track:
  - during a preview of this adjacency, the preview's voice for that side, as now (its
    "has not started yet" / "has ended" / "fallen silent" notices stay);
  - otherwise the attended voice when its row's track is that track, or the incoming voice
    of a live crossfade when its track is;
  - otherwise nothing is captured (null), with a notice: "Nothing captured: *track* is not
    playing." That should be unreachable from the editor, whose button is greyed, but the
    call stays total.
  A track matches by its file path, as the store keys it. With the same file twice in the queue,
  either row's voice is that track. Repeat-one's this → this matches on both sides.
- **Whether a button is live is the engine's to say:** `CanCapture(adjacency, side)`, asked by
  the editor's `Refresh` on each render. It is true exactly when `Capture` would find a voice.
  During a preview it stays true for both sides while the preview runs, as now, since the early
  or late press is told by notice (ADR 0003). The shell compares no tracks (CLAUDE.md test 2).
- **The words.** Each Capture button's tooltip reads "Capture *track title*'s playhead" (the
  Presenter's track name). WinForms shows no tooltip on a disabled button, so while a button is
  greyed with an adjacency open (not gone), a grey line under its field reads "Play *track* to
  capture", or "Preview to capture" when that reads better. It is hidden while the button is
  live and when no adjacency is open. The Presenter words both.
- **From end** is passed through as now: the point is measured from the end of the matched
  track.
- **Gone adjacencies** keep their buttons greyed, as now, with no hint line: a gone track
  cannot be played.
- **ADR 0003** gets a dated amendment: capture reads the open adjacency's own tracks, never
  merely the attended voice.

## Testing decisions

Core, on both targets, through the engine:
- A playing, B → C passed: `Capture(B → C, FadeOut)` returns null with the notice, and
  `CanCapture` is false for both sides.
- B attended (playing B): the fade-out capture for B → C returns B's playhead; `CanCapture`
  true for the fade-out side and false for the fade-in side (C is not heard).
- During a live crossfade B → C: the fade-in capture reads C's incoming voice; the fade-out
  capture reads B.
- From end measures from the matched track's length.
- A preview of B → C: both sides as before (the existing preview capture tests pass unchanged);
  `CanCapture` true for both while it runs.
- Repeat-one A → A: both sides read A.
- Callers of the old `Capture(side, fromEnd)` move to the new call. The tests that captured
  "the attended voice" for an adjacency of other tracks change to the new rule.

`Faderio.Shell.Tests` (both targets), the panel against a real engine:
- A playing, the editor on B → C: both Capture buttons greyed, the hint line under the
  fade-out point names B, and a click on Capture does nothing.
- B playing, the editor on B → C: the fade-out Capture live with the tooltip naming B, its hint
  hidden; the fade-in Capture greyed, its hint naming C. Clicking the live one writes B's
  playhead and saves it.

Both **Looks** halves change their capture items: a Capture button is live only while its own
track plays (or a preview of that boundary runs), its tooltip names the track, and while greyed
the line under the field says what to play. The **Sounds** half keeps "capture at a drum hit;
the fade starts on the hit", now with the hit's track playing.

## Acceptance criteria

- [x] A capture never writes another track's playhead into a point.
- [x] A Capture button is live only while its own track is heard, in both hosts.
- [x] The editor says which track a capture reads, and what to play when it cannot.
- [x] Preview captures as before.

## Comments

- **Resolved 2026-10-02.** Built as specified, with these choices:
  - **Engine:** `Capture(adjacency, side, fromEnd)` and `CanCapture(adjacency, side)` share
    one lookup, `VoiceFor`: a preview of the same pair of files gives its voice for the side;
    otherwise the attended or incoming voice whose track has the side's file path. `CanCapture`
    reads the published view without the lock, as `AdjacenciesOf` does. A paused queue's
    attended voice still counts, as it did before (capture while paused at a spot). So does the
    held queue's voice during a preview of *another* adjacency. That case is unreachable from the
    editor, which stops a preview when it opens another adjacency.
  - **Words:** the tooltip is "Capture *title*'s playhead". The hint is one form, "Play *title*
    or preview to capture", since a preview always serves. The engine's notice names the file
    stem, as its other notices do.
  - **Layout:** the two hint lines pushed Render below the fold at the default 1240 × 600
    window. The panel was tightened to fit again: field label rows 24 → 20, curve row 34 → 28,
    the editor's buttons 32 → 30, From end with no vertical margin. UIA reports no scrollable
    pane in the editor at the default size.
  - **Tests:** `CaptureOwnTracksTests`, 8 tests on both targets; `EditorCapturesOwnTracksTests`,
    3 tests on both targets. The old single-track callers capture through `AdjacencyOf(a, a)`.
  - **Smoke (standalone).** The build on a scratch library with tone-a, tone-b and tone-c, editor
    on tone-a → tone-b:
    - ✅ Stopped: both Capture buttons greyed, "Play tone-a or preview to capture" and "Play
      tone-b or preview to capture" under the fields, Render in view.
    - ✅ tone-a playing: the fade-out Capture live and its line gone, the fade-in one greyed with
      its line. A click saved 4.44 s ("Saved tone-a → tone-b.").
    - ✅ After the queue moved on to tone-b, the fade-in side was live and the fade-out greyed.
      A press that raced the handover was told "Nothing captured: tone-a is not playing."
    - ✅ Preview: both live.
    - Not seen: the tooltips (WinForms tooltips are not UIA help text).
    - The MusicBee Looks item was not run in this session.
