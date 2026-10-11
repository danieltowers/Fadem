# The playhead comes from the device clock, and it is the only position the shell sees

Status: accepted

faderio's promise is that an authored fade point lands where the user placed it **by ear**, so
the position the user interacts with must be the one they are *hearing*, not the one the
decoder has read ahead to. We split position into two quantities with distinct types — the
**playhead** (audible, public) and the **decode position** (engine-internal, for scheduling) —
and derive the playhead at the output boundary from frames the device reports it has actually
emitted, rather than by subtracting a latency constant.

Decided in [ticket 03](../../.scratch/202609-faderio-transition-you-can-trust/issues/03-what-position-does-set-from-here-capture.md),
which holds the detail. Terms are defined in [CONTEXT.md](../../CONTEXT.md).

## Considered options

**A latency constant per output mode** (v2's 100 / 50 / 150 ms). Rejected: a constant is wrong
on some hardware by construction. Both `WasapiOut` and `WaveOutEvent` implement
`NAudio.Wave.IWavePosition`, so a measurement is available for one interface check. The
constant survives only as a fallback for an output that cannot be queried.

**Compensating inside the engine, or in the shell.** Rejected: latency is the output's
property, so the engine would be subtracting a number it never measured, and the shell holding
a correction is v2's defect exactly — there, the shell read the decode position straight into
the capture field and every point authored by ear was stored late.

**Snapping the playhead to the new position immediately after a seek.** Rejected, though it was
cheaper. A seek does not flush the device buffer, so for up to one buffer the listener is still
hearing the old position. The playhead stays honest through that window, which costs each voice
a short history of reference points but makes the invariant unconditional: *the playhead always
describes sound the listener can actually hear.*

**Compensating for human reaction time at capture.** Rejected: it would make the stored point
differ from the measured one, which is the bug class this decision exists to eliminate.

## Consequences

`IAudioOutput` grows a member reporting frames emitted; it carried no position or latency fact
before. Frames-emitted and the mixer timeline **must share an origin** — output recreation and
engine reset have to be the same reset, or the playhead drifts after a stop or an output-mode
change.

Position becomes a property of a **voice**, not of the player, which is what makes the
crossfade case a property read: during a transition the outgoing voice stays attended until it
falls silent, instead of the readout jumping to the incoming track before it is audible.

A reader coming from v2 will find its planning docs arguing the opposite — *"output latency
does not affect fade timing or alignment... it only shifts audibility."* That is true of the
**trigger** side and false of the **authoring** side, and the authoring side is where the human
is. Transitions are still scheduled against the mixer timeline; nothing here changes that.

## Addendum: the rule, stated generally (2026-09-23)

The `View` describes what the listener hears: the playhead, the attended voice, the now-playing
row, *Stopped*, the clip light and a preview's phase all follow audible time. Commands take
effect at the decode position as soon as possible and are heard one device buffer later; the
engine never delays a command to line it up with the playhead. Settings in the `View` show
their value as set, and notices carry no position. Settled in
[When does ReplayGain switch, and is it on by default?](../../.scratch/202609-faderio-phase-1-restage/issues/06-when-does-replaygain-switch-and-is-it-on-by-default.md).
