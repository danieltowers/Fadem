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
