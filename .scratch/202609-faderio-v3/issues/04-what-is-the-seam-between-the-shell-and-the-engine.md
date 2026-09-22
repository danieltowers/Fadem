# What is the seam between the shell and the engine?

Type: grilling
Status: resolved
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

**The shell gets a command surface and an immutable view, and nothing else.** No live
collection crosses the seam; every command is total; state is published after the engine's lock
is released, through a sink the shell supplies. The four v2 defects this ticket names — the
shuffle-empties-queue bug, the hand-rolled `JumpTo`, the ListView drifting, the shell knowing
engine invariants — become unwritable rather than discouraged.

Reached by **Design It Twice**: four sub-agents each produced a radically different interface
under a different constraint (minimal, flexible, common-caller, ports-and-adapters). All four
independently converged on the same substance — immutable versioned snapshot, no live
collection, total commands, publish-after-lock, engine-issued track length — and differed only
in packaging. That convergence is the strongest evidence this session produced. The chosen
shape is the common-caller design with three grafts.

### The interface

```csharp
// ── Units. Frames never cross the shell seam. ──────────────────────────────
public readonly record struct Frames(long Count);        // engine frames; output seam ONLY
public readonly record struct Length(Frames Frames)  { int Milliseconds; double TotalSeconds; }
public readonly record struct Playhead(Frames Frames)     // audible, live, attended voice
{   int Milliseconds; double TotalSeconds;
    static Playhead FromMilliseconds(int ms);             // the seek bar's Value, straight through
    static Playhead At(Point p); }                        // explicit only: audition a point
public readonly record struct Point(Frames Frames)        // authored, stored
{   double TotalSeconds;
    static Point FromSeconds(double s); }                 // typed text — the ONLY shell→Point path
// No Playhead→Point conversion exists. Capture() is the only sound→Point path.
// Decode position has no public type.

// ── View: immutable, complete, render-from-able ───────────────────────────
public readonly record struct RowId(long Value);          // engine-issued; stable across shuffle
public sealed record QueueRow(RowId Id, Track Track, Length Length, bool IsCurrent);
public sealed record VoiceView(QueueRow Row, Playhead Playhead, Length Length) { Playhead Remaining; }
public sealed record PlayerView(
    long Version, long QueueVersion, Transport Transport,
    IReadOnlyList<QueueRow> Queue,          // fresh immutable list per QueueVersion
    VoiceView? Attended,                    // null iff Stopped; outgoing until it falls silent
    VoiceView? Incoming,                    // non-null only during overlap
    bool Shuffle, RepeatMode Repeat);
public sealed record AddReport(IReadOnlyList<QueueRow> Added, IReadOnlyList<(string Path, string Reason)> Failed);

// ── The sink: how the shell is told. A port with three adapters. ─────────
public interface IEngineSink { void Changed(PlayerView view); void Notice(string message); }
// PostingSink(SynchronizationContext) — production, marshals to the UI thread
// ImmediateSink                       — headless Form test, runs inline on the STA thread
// RecordingSink                       — Core tests: ordered log of (Version, view) for asserting
//                                       WHICH version moved AttendedRow and at what FramesEmitted

// ── The engine ─────────────────────────────────────────────────────────────
public sealed class PlaybackEngine : IDisposable
{
    public PlaybackEngine(IAudioOutput output, ITransitionStore store, IEngineSink sink);
    public PlayerView View { get; }                       // lock-free read of an immutable record

    // Queue — the only way rows come to exist or move
    public AddReport AddTracks(IEnumerable<string> paths);
    public void Remove(RowId row); public void Clear();
    public void SetShuffle(bool on);                      // original order recorded HERE
    public void SetRepeat(RepeatMode mode);

    // Transport
    public void PlayRow(RowId row);                       // replaces stop→set-current→play
    public void Play(); public void Pause(); public void TogglePlayPause(); public void Stop();
    public void Next(); public void Previous();
    public void Seek(Playhead target);                    // attended voice; cancels a transition in flight

    // Authoring
    public Point? Capture();                              // attended playhead, in frames, unquantised
    public TransitionSpec? Transition(Adjacency a);
    public void Author(Adjacency a, TransitionSpec spec); // upserts AND reschedules a pending boundary
    public void Unauthor(Adjacency a);
}

// ── The output seam ────────────────────────────────────────────────────────
public interface IFrameSource { Frames Read(Span<float> interleaved, Frames wanted); }
public interface IAudioOutput : IDisposable
{
    void Initialise(IFrameSource source);
    void Play(); void Pause(); void Stop();               // Stop resets FramesEmitted: shared origin
    Transport State { get; }
    Frames FramesEmitted { get; }                         // audibly emitted; the playhead's source
    event Action<Exception?>? Stopped;
}
// NAudioAudioOutput · FakeAudioOutput (Advance(Frames) — it IS the clock) · WavFileAudioOutput

public static class Renderer { public static void RenderBoundary(Adjacency a, ITransitionStore store, string wavPath); }
```

### Invariants the shell cannot break

- **No live collection exists on this side of the seam.** `View.Queue` is a value. Tracks only
  enter through `AddTracks`, which is where shuffle's original order is recorded.
- **`Attended.Playhead ≤ Attended.Length` always**, so `_seek.Value ≤ _seek.Maximum` by
  construction. `Remaining` is never negative.
- **`Attended` is the outgoing voice until it falls silent.** The swap to the incoming voice
  and the `IsCurrent` flip happen in the same `View` publication, at the audible handover.
- **`Track.Length` is engine-issued** — `Track` has an internal constructor. Which length the
  engine issues is [ticket 10](10-which-length-is-a-track.md)'s call; the seam holds either way.
- **`Capture()` returns the same value `View.Attended.Playhead` would have.** The shell never
  touches a number, so it cannot quantise it.
- **`Author()` on the in-flight adjacency reschedules.** Editing a transition during the very
  playback it governs is the slice's main loop; v2 rescheduled only by the accident of a seek.
- **A stale `RowId` is a `Notice`, never an exception.** Every command is total; only
  `ObjectDisposedException` escapes.

### Threading

Commands from any thread, serialised inside. `View` is a volatile read of an immutable record —
never a lock, never blocks rendering. **Sink calls are queued under the lock and drained after
it is released**, so no handler ever runs under `_sync` and re-entrant commands from a handler
are legal. Which thread calls the sink is the output adapter's private fact; the fake has no
render thread, so with `ImmediateSink` the headless Form test is fully synchronous with no
message pump. `Changed` is coalesced: at most one pending delivery per sink.

Continuous state (the playhead) is **polled** on the shell's own timer from `View`; discrete
state (queue, transport, attended row) is **pushed** through the sink. The timer was never
v2's bug — the scaling was — and polling decouples refresh from block size across the three
output modes.

### Typed units: frames never cross the shell seam

Externally: `Playhead`, `Point`, `Length`, and `TimeSpan` for durations. `Frames` appears on
exactly one seam, `IAudioOutput`, so a samples count cannot cross it — v2's
`FramesToSamplesProvider` bug becomes a compile error at the adapter edge. *Samples* appear in
no public signature anywhere. Inside the mixer, plain `long`; a `Frames` type for internal
discipline is noted against [ticket 07](07-migrate-faderio-core-into-v3.md).

### The placement rule

Three mechanical tests, each checkable by reading a diff, written into `CLAUDE.md`:

1. **One user intent is one engine call.** If the shell sequences two engine members to do one
   thing the user asked for, the intent is missing from the engine.
2. **The shell formats engine values; it never computes from them.** If a number needs
   arithmetic, the engine returns the result.
3. **The shell holds no engine state between calls.** It reads, renders, forgets.

Each names a v2 defect: `JumpTo` (1); seek scaling, remaining-time subtraction, from-end
conversion, `"0.###"` quantisation (2); the ListView drifting from the queue (3).

### What the shell becomes

```csharp
_engine = new PlaybackEngine(OutputFor(args), store, new PostingSink(SynchronizationContext.Current));
_timer.Tick += (_, _) => Render(_engine.View);           // sink.Changed also calls Render

void Render(PlayerView v)
{
    _playPause.Text = v.Transport == Transport.Playing ? "⏸" : "▶";
    if (v.QueueVersion != _shownQueue) { RebuildRows(v.Queue); _shownQueue = v.QueueVersion; }
    var a = v.Attended;
    _seek.Maximum = a?.Length.Milliseconds ?? 0;           // no scaling, ever
    if (!_seek.Capture) _seek.Value = a?.Playhead.Milliseconds ?? 0;
    _time.Text = a is null ? "" : $"{Fmt(a.Playhead)} / -{Fmt(a.Remaining)}";
}
_queueList.DoubleClick += (_, _) => _engine.PlayRow(RowOf(_queueList.SelectedItems[0]));
_seek.MouseUp        += (_, _) => _engine.Seek(Playhead.FromMilliseconds(_seek.Value));
_captureOut.Click    += (_, _) => { _fadeOut = _engine.Capture(); _fadeOutText.Text = Fmt(_fadeOut); };
```

The presenter holds exactly ticket 06's list: `Fmt`/`ParseSeconds`/`Point.FromSeconds`, `RowOf`
and `AdjacencyAt(row)` with its bounds check, `Summarise(AddReport)`, and `OpenTransitionStore`.
`_shownQueue` and `_fadeOut` are the only fields.

### What the implementation hides

The mixer and its timeline; decode position and the per-voice reference history that keeps the
playhead honest through the post-seek drain; boundary scheduling and the resolver; the attended
voice rule and audible-handover detection; shuffle's original order; `RowId` allocation and
current-index fix-ups on remove; metadata reading and per-file failure; output lifecycle with
recreate-and-reset sharing one origin; the lock and the deferred sink drain; change coalescing;
that a transition edit mid-track reschedules. `PlaybackQueue`, `TransitionResolver`,
`MixerVoice`, `EffectiveTransition` all become `internal`. `Repeat`/`Shuffle` setters, `Output`,
`Resolver` and the shared-ownership `PreviewSession` — the four leaked mutables and two unlocked
setters the survey found — no longer exist on the surface.

### Dependencies and adapters

| Dependency | Category | At the seam |
|---|---|---|
| Output device | true external, real fake | `IAudioOutput`: NAudio · Fake (the clock) · WavFile |
| Notification | — | `IEngineSink`: Posting · Immediate · Recording |
| Transition store | local-substitutable | `ITransitionStore`: JSON on a temp path in tests; the shell opens/recovers it and hands it in |
| Decoders | true external, no fake | **not a port** — one adapter is a hypothetical seam; tests use real files |
| Clock | = fake output | nothing separate; a second clock would be a second origin |
| WinForms | outside | `Render(PlayerView)` plus one-line handlers |

### The four designs, for the record

- **Minimal** — `Apply(command)` + `Changed(snapshot)`, seventeen command records, snapshot per
  block, display timer eliminated. Rejected on discoverability: the command hierarchy is the
  real vocabulary and an IDE cannot help with it.
- **Flexible** — four sub-interfaces, an `IObservableValue<T>` primitive, `Facet<T>()`
  locator. Rejected as the widest interface for behaviour the slice does not yet need; its
  opaque `QueueRowId` was taken.
- **Common-caller** — the base. Its type-system line (`Capture()` the only sound→`Point`
  path) and `Author()`-reschedules were the two sharpest observations of the four.
- **Ports** — same skeleton plus five ports. Its `IEngineSink` with a `RecordingSink` was
  taken; its stale-index exception was not.

### Facts this rests on

- Four live mutables leak from v2's engine (`Queue`, `Resolver`, `Output`, `PreviewSession`)
  plus two unlocked public setters (`Repeat`, `Shuffle`). `PlaybackQueue` has no locking; the
  engine reads it from the render thread while the shell mutates it from the UI thread.
- Every v2 event is raised while `_sync` is held; three of four fire from either thread
  depending on the path. The shell survives via `BeginInvoke` only.
- All v2 domain types are immutable `sealed record`s. Only `PlaybackEngine` and
  `PlaybackQueue` are mutable public classes.

### Surfaced, not settled here

- **Decoded length.** All four designers chose it unprompted → recorded on ticket 10.
- **Internal `Frames` discipline** and **`Track` internal constructor** → ticket 07.
- **`Renderer` as a static entry point; sink adapters** → ticket 11.
- **Preview** as a second engine cannot open WASAPI-exclusive twice → the map's preview fog.
- **The build decomposition** — this was the last decision gating it. Graduated into tickets
  12, 13 and 14.

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
