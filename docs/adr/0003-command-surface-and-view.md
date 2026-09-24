# The shell gets a command surface and an immutable View, and nothing else

Status: accepted

v2's spec asked for a thin shell and got one, and every defect the user hit still lived in it,
because the shell was handed the engine's live internals — the mutable queue, the resolver, the
output, a shared-ownership preview session, two unlocked setters — and had to know engine
invariants to use them correctly. It didn't: add-files bypassed the one method that recorded
shuffle's original order, so shuffle on-then-off emptied the queue; a jump was hand-rolled as
stop→set-current→play. The engine now exposes intent-shaped commands and a versioned, immutable
`View`, and no live collection crosses the seam. Every command is total. State is published after
the engine lock is released, through an `IEngineSink` the caller supplies.

Decided in [ticket 04](../../.scratch/202609-faderio-v3/issues/04-what-is-the-seam-between-the-shell-and-the-engine.md),
which holds the full interface sketch. The placement rule is in [`CLAUDE.md`](../../CLAUDE.md).

## How it was reached

Design It Twice: four sub-agents each produced a radically different interface under a different
constraint — minimal, flexible, common-caller, ports-and-adapters. All four independently
converged on the same substance: immutable versioned snapshot, no live collection, total commands,
publish-after-lock, engine-issued track length. They differed only in packaging. That convergence
is the evidence for the invariants; the packaging choice below is the smaller call.

## Considered options

**A safe queue object** — keep `engine.Queue` but make it lock and validate. Rejected: the shell
would still hold a handle to engine state and still need to know when to call what. Unrepresentable
beats validated; the shuffle bug cannot be written against a `View`.

**Command records dispatched through one `Apply(command)`** (the minimal design). Rejected on
discoverability: seventeen record types are the real vocabulary and an IDE cannot help with them.
Its one lasting contribution — every command is total and a misuse is a `Notice` — was kept.

**Sub-interfaces plus a `Facet<T>()` locator** (the flexible design). Rejected as the widest
interface for behaviour the slice does not yet need; the flexibility was for us, not for strangers.
Its opaque `RowId` was kept, so a stale row is a `Notice` rather than an exception.

**A `SynchronizationContext` constructor parameter** for marshalling. Rejected in favour of an
`IEngineSink` port (from the ports design): three adapters justify it — `PostingSink` for the UI
thread, `ImmediateSink` for the headless Form test, and `RecordingSink`, which lets a Core test
assert *which* `Version` moved the attended row and at what `FramesEmitted`. The engine never
learns the word "thread".

**Typed frames everywhere, including the shell seam.** Rejected: the shell never needs frames.
`Frames` appears on exactly one seam, `IAudioOutput`, which is where v2's frames-vs-samples defect
lived. Externally the seam carries `Playhead`, `Point`, `Length` and `TimeSpan`.

## Consequences

`Author()` reschedules a pending boundary. Editing a transition during the very playback it
governs is the slice's main authoring loop; v2 rescheduled only by the accident of a seek.

`Capture()` is the only sound→`Point` path and `Point.FromSeconds` the only text→`Point` path;
no `Playhead`→`Point` conversion exists. The glossary's definition of capture is a compile-time
fact, and the shell cannot quantise a captured point because it never touches a number.

`Track` has an internal constructor, so its length is always engine-issued. Which length — decoded
or tagged — is ticket 10's; the seam holds either way.

Continuous state (the playhead) is polled from `View` on the shell's own timer; discrete state is
pushed through the sink. The timer was never v2's bug — the scaling was.

Decoders are not a port. One injectable adapter is a hypothetical seam, and tests use real files
by decision (ADR 0002).

The shell's whole display loop is one `Render(PlayerView)` of about ten lines. Its presenter holds
exactly: seconds parse/format, row→adjacency with a bounds check, add-report summary, and
transition-store open/recovery. Anything else that appears there fails the placement rule.

## Addendum: adding is a background run, and its outcome is a notice (2026-09-23)

`Add(paths)` returns at once and probes off the engine lock, one row appended under it per
file, with the run's progress in `View.Adding` and `CancelAdd()` to end it. Its outcome
reaches the user after the command has returned, so it cannot be a return value the shell
formats: when a run refuses anything, the engine raises one notice naming every rejection, and
a run that refuses nothing raises none. The add-report summary therefore moved from the
presenter to `AddReport`; the presenter formats the progress instead. `Rejection` stays
structured as ADR 0007 requires; only the one sentence the notice carries is worded in Core. Settled in
[What does queue management add?](../../.scratch/202609-faderio-phase-1-restage/issues/12-what-does-queue-management-add.md)
and built in [issue 01](../../.scratch/202609-faderio-phase-1-build/issues/01-adding-runs-in-the-background.md).

`Add` also takes folders ([issue 02](../../.scratch/202609-faderio-phase-1-build/issues/02-add-a-folder-or-drop-files-on-the-queue.md)).
The worker lists a folder off the lock when the add reaches it and puts its files where the
folder stood, so a drop of files and folders keeps its order. Until then the folder counts as
one path in `View.Adding`. **Add files…**, **Add folder…** and a drop from Explorer each pass
their paths straight to `Add`; the shell never lists a directory.

`Add` also opens `.m3u` and `.m3u8` playlists
([issue 03](../../.scratch/202609-faderio-phase-1-build/issues/03-open-and-save-playlists.md)),
read off the lock like a folder with their entries put where they stood. Opening a playlist is
adding it, so it appends; there is no "replace the queue". `SaveAsPlaylist(path)` writes the
visible order, taking the rows under the lock and writing the file off it, and reports how it
went as a notice. **Open playlist…** and **Save as playlist…** are two file dialogs; the shell
never reads a playlist or reads `View.Queue` to write one.

## Addendum: the presenter picks the repeat button's next mode (2026-09-24)

The shuffle toggle and the repeat button ([issue 07](../../.scratch/202609-faderio-phase-1-build/issues/07-shuffle-and-repeat-controls.md))
are one command each, `SetShuffle(!View.Shuffle)` and `SetRepeat(next)`. The cycle *off → all →
one* is a display choice over the enum, not engine arithmetic, so `Presenter.NextRepeat` picks
the next mode from the one the view holds when the button is pressed. It joins the presenter's
list, with the glyph and tooltip for each state. The buttons read the view when pressed, not
the state they last showed, so a change made elsewhere is never undone by a stale button.

## Addendum: the view says whether a command would act (2026-09-24)

Transition now ([issue 08](../../.scratch/202609-faderio-phase-1-build/issues/08-transition-now.md))
does nothing while paused or stopped, with no next adjacency, and while a transition is in
flight. "In flight" includes a hard cut or the final finish still in the device buffer, which
no other view field shows. So the engine publishes `View.CanTransitionNow`, derived from the
same frames-emitted instant as the playhead, and the command guards on that same flag. The
shell sets the button's `Enabled` from it and does no logic of its own. If another command
gains a no-op condition the shell must show, it gets a flag the same way.

## Addendum: authoring reports its outcome, and the shell keeps the session's notices (2026-09-24)

A store that cannot be written made `Author` and `Unauthor` throw, against "every command is
total" ([issue 09](../../.scratch/202609-faderio-phase-1-build/issues/09-notices-a-strip-and-a-session-list.md)).
Both now catch the write failure under `Run` and raise a notice, and both report success as a
notice too, as `SaveAsPlaylist` does. The editor no longer says "Saved" itself, so a failure
can never follow a confirmation. `JsonTransitionStore` keeps a change only once it is on disk,
so "not saved" is true: nothing plays that the file does not hold. `Adjacency.Name` is the one
wording of an adjacency, used by the engine's notices and the editor's title.

The shell keeps a `NoticeLog` of what the sink has delivered this session: a repeat merges into
its first entry with a count. It holds delivered messages, not engine state, so it passes the
placement rule. The presenter gains the strip's wording (`FmtNotice`, `FmtNoticeCount`). The
store open path reports its warnings as startup notices instead of a message box.

## Addendum: settings are one command, and the view carries them (2026-09-24)

The engine owns `settings.json` through `SettingsStore`, as it owns the transition store
([issue 12](../../.scratch/202609-faderio-phase-1-build/issues/12-settings-a-file-a-dialog-and-the-default-crossfade.md)).
`ChangeSettings(settings)` is the one command: it saves the file, applies each value and
reports the outcome as a notice, as authoring does. A file that cannot be written changes
nothing. `View.Settings` carries the settings in force, so the dialog opens on the view and
holds nothing between calls. The output mode in the view is the one chosen for the next launch
(ADR 0008). The mode in use belongs to the output adapter, not the engine, so the composition
root hands the form a read of it, as it already did for the title. `Presenter.FmtModeInUse`
words the difference. The store's trouble at launch (a corrupt file moved aside, a newer
schema) reaches the sink from the engine's constructor, before any lock is taken.

## Addendum: `Author` refuses impossible values and returns them (2026-09-24)

`Author` now returns the values it refused, each as a `Refusal` naming its `TransitionField`
and a reason worded for the user
([issue 14](../../.scratch/202609-faderio-phase-1-build/issues/14-editor-curve-from-end-revert-remove-validation.md)).
Only the engine holds both tracks' lengths, so only it can say a point is past the end
(placement test 2). A refusal is data the editor shows beside each field, not an exception:
every command stays total. The outcome is also a notice, as for any save. A duration that only
runs past the end is not refused; the resolver shortens it. A spec with all four parameters
blank removes the transition instead of storing it, so the store never holds an empty one.

The from-end arithmetic is the engine's too. `Capture(fromEnd: true)` returns `Length −
playhead` of the attended track, and `FromOtherEnd(track, point)` gives the same moment
measured from the other end, for ticking and unticking **From end**. The shell never subtracts
from `Length`. The presenter gains the fade curve's name (`FmtCurve`), which the editor's list
shows.
