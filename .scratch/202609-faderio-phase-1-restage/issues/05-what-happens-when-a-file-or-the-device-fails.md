# What happens when a file or the device fails?

Type: grilling
Status: resolved
Blocked by: 01, 02

## Question

v2 issue 0014: one bad file never stops playback. The slice kept only the narrowest sliver: a
file that fails to open at add time is rejected with a message. Decide the rest:

- **A track fails at play time** (deleted, moved, truncated, a network drive gone after it was
  added). Skip it with a notice? What happens to the adjacency's transition when its incoming
  track cannot open, given ticket 02's skip rules?
- **Audio ends before `Length`.** ADR 0010 says one notice and the track ends there; confirm
  how that interacts with a scheduled fade-out point beyond the real end.
- **Device failure.** WASAPI fails to initialise, or the device disappears mid-play (headphones
  unplugged). v2 fell back to WaveOut and surfaced the downgrade. Which fallbacks, and does the
  fallback change the engine rate (ADR 0008 fixes it at launch)?
- **Where notices live.** The status strip, a log, both; and whether an engine error can ever
  be swallowed (v2 said never).

## Comments

- From [What is preview?](04-what-is-preview.md): a preview plays through the main engine on
  its own voices while the queue is held. Decide what a decode failure or device loss during a
  preview does too, and whether the held queue survives it.

## Answer

Grilled with the user on 2026-09-23, starting from the findings in
[Audit the engine against v2's intent](01-audit-the-engine-against-v2s-intent.md) (items 9–13).
`CONTEXT.md` gains **Unreadable**, and **Rejected** is amended: a queued track was promised
when it was probed, and a file that later becomes unreadable is not a rejection.

### A track fails at play time: unreadable

- **Passed over, row kept.** A queued track whose file cannot be read at play time (deleted,
  moved, truncated, on a drive that has gone) is passed over with a notice. Its row stays in
  the queue, marked unreadable; `QueueRow` gains the mark. It is tried again whenever the queue
  reaches it, and the mark clears the next time the file opens. Passing over reuses today's
  `StartTrack` catch; everything unreadable still finishes playback.
- **Your own navigation.** ⏭ and Transition now pass over unreadable rows to the next readable
  one. ⏮ passes over them backwards. Double-clicking an unreadable row tries it; if it fails,
  a notice appears and the playing track carries on. An explicit choice never jumps to a row
  you did not pick.

### An incoming track fails: found at scheduling, not at the fade

The engine opens the incoming track **when it schedules the boundary**, as the outgoing voice
starts, not when the fade begins. If it fails, it is marked unreadable and the boundary is
re-targeted to the next readable row. That adjacency (A → C) resolves as usual: its own
transition, or a hard cut if none is authored. A fade into silence is never heard. A file that
fails between that open and the fade falls under the next rule.

### A decode error mid-track: the audio ends there

This is treated exactly like audio that ends before `Length` (ADR 0010): one notice, the track
ends where the error happened, and the boundary fires there. A fade-out point beyond that
point behaves as it does for a short file. This replaces the per-block silence and notice
storm the audit found in `EngineFrameSource.Read`.

### Device loss

- **The engine pauses** at the heard playhead with a notice and releases its voices. It is
  never half-stopped. It does not resume by itself: sound would go to speakers you just
  unplugged your headphones from.
- **Mid-transition,** it lands paused on the transition's incoming track at its heard
  position, as a skip would.
- **Play reopens** the output on the current default device and plays on. If that device's
  rate differs from the engine's, the output converts and a notice says to restart to mix at
  the device's rate. The engine rate never changes at runtime (ADR 0008). If no device is
  available, Play leaves playback paused with a notice. Whether NAudio's shared-mode
  `WasapiOut` resamples automatically on a mismatch is a fact for the build to confirm.

### When the output fails at launch

The fallback chain is exclusive → shared → WaveOut, with a notice at each step. The engine
takes its rate from the output it actually opened, still fixed at launch. The title or status
names the mode in use, not the one requested.

### Failures during a preview

- If either track is unreadable, the preview does not start: a notice appears, the row is
  marked, and the queue is never held.
- A decode error mid-preview ends that voice, and the preview carries on to its normal end.
- Device loss ends the preview, and the held queue stays paused where it was held.

### Notices

- **Where.** The status strip shows the latest notice and a count. Clicking it opens this
  session's notice list. There is no log file.
- **Nothing is swallowed.** Every engine error becomes a notice. That includes the store write
  failure escaping `Author` and `Unauthor` (every command stays total) and the empty `catch`
  in `Presenter.OpenTransitionStore`. A repeated notice is merged into its first one with a
  count.

### Consequences for the map

**Unmeasured audio paths** (fog) now also covers device loss, reopening, and the launch
fallback chain. All of them live in `NAudioAudioOutput`, which no automated test reaches.
