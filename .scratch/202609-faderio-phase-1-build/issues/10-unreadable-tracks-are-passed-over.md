# 10: Unreadable tracks are passed over

Status: resolved
Blocked by: 06, 09

Decided in [What happens when a file or the device fails?](../../202609-faderio-phase-1-restage/issues/05-what-happens-when-a-file-or-the-device-fails.md)
(the unreadable, incoming and decode-error sections). **Unreadable** is in `CONTEXT.md`.
Carries v2 0014's intent (story 36). Closes audit findings 9 and 10.

## What to build

- **An unreadable track is passed over with a notice.**
  - `QueueRow` gains the unreadable mark, and the row stays in the queue.
  - The track is tried again whenever the queue reaches it, and the mark clears when the file
    opens.
  - ⏭, ⏮ and Transition now pass over it.
  - Double-clicking it tries it once. If that fails, the playing track carries on.
- **The incoming track opens when its boundary is scheduled.** If it fails, it is marked and
  the boundary is re-targeted to the next readable row, whose adjacency resolves as usual.
- **A decode error mid-track is treated exactly like audio that ends early** (ADR 0010):
  - one notice;
  - the track ends there;
  - the boundary fires there.

  This replaces the per-block silence and notices in `EngineFrameSource.Read`.

## Where the logic lives

- **Engine:** all of it. The shell renders the mark from `QueueRow`.

## Acceptance

- [x] A queued file deleted before its turn is passed over with one notice. Its row stays,
      marked. Restoring the file and reaching it again plays it and clears the mark.
- [x] With A→B authored and B deleted, A never fades into silence; the boundary goes to the
      next readable row with that adjacency's own resolution.
- [x] A file that fails to decode mid-stream produces one notice and a boundary at that point.
- [x] ⏭ and ⏮ pass over marked rows; double-clicking a marked row that still fails leaves the
      current track playing.

## Tests

- **fake:** each case. A decode error can be simulated with a fixture truncated after its
  header plus a seam that fails reads at a given frame; choose the smallest seam that is
  honest.
- **form:** the mark is rendered.

## Smoke

- **Looks:** an unreadable row is visibly marked.
- **Sounds:** rename a queued file before its turn; the queue plays on past it without a gap
  or a fade into silence.

## Comments

- Built 2026-09-24. `QueueRow.IsUnreadable` is the mark. The shell draws it as "⚠" before the
  row number, greyed, with a tooltip saying it is passed over and that a double-click tries it
  again.
- **One way in.** Every track the engine plays is opened by `TryOpen`: it marks the row, or
  clears the mark when the file opens. Moving on goes through `OpenFirstReadable`, which walks
  rows with `PlaybackQueue.Walk` and gives one "Passed over …" notice per row that fails. Play,
  the natural advance, ⏭, ⏮, Transition now and Remove's replacement all use it. The old
  `SkipToNextAfter` is gone.
- **When a marked row is tried again.** It is retried when the queue reaches it, meaning when
  the track before it starts and schedules its boundary; by Play; and by a double-click. ⏭, ⏮,
  Transition now, Remove's replacement and any reschedule (queue edit, seek, repeat) pass over
  a marked row without retrying it. So a background add that reschedules once per file never
  repeats the notice. `PlayerView.NextAdjacency`, and so `CanTransitionNow`, passes over
  marked rows too.
- **The incoming track opens at scheduling, and is held.** The source opened for the boundary
  is kept in `_prepared` and taken, not reopened, when the boundary starts it. A reschedule
  whose target has not changed reuses it, so a queue change costs no file I/O. Hard cuts now
  have a `BoundarySchedule` too, so every boundary knows its incoming row.
- **Decoders open files with `FileShare.Read | Delete`,** so a held file never stops the user
  renaming or deleting it. **Consequence for the Sounds smoke line:** a file renamed after the
  track before it has started still plays, because it is already open. To see it passed over,
  rename it before the track ahead of it starts. `docs/smoke.md` says so. If the user wants a
  rename during the previous track to count, the choice is between not holding the file (open
  to check, close, reopen at the fade, which brings back audit finding 10's race) and holding
  it without delete sharing (Explorer then refuses the rename).
- **Decode errors.** `TransitionMixer` catches a failing `Source.Read` for that voice only,
  records it in `MixerVoice.DecodeError`, and ends the voice at the start of the read that
  failed; that read delivered nothing. The engine raises one notice ("… could not be read past
  n s, so it ends there: …") instead of the shortfall notice. The boundary fires there, as for
  a short file. `EngineFrameSource.Read` keeps its catch, but only as a guard against engine
  faults: removing it would have thrown them into NAudio's thread.
- **Short audio before a fade-out point** (ADR 0010, and now decode errors too): when the
  outgoing voice ends before its boundary's trigger, the incoming track starts where it ended,
  with its authored fade-in. The old code spawned it at the trigger, leaving a gap of silence.
- **Open edges.**
  - Retrying marked rows at a boundary opens files on the render thread, as opening the
    incoming track at the fade always did. A drive that hangs on open could cause a dropout.
  - Under repeat-one, if the playing file is renamed, the next loop's open fails. The playing
    row is then marked, and the queue moves on to the next row.
  - A truncated **FLAC** crashes the process inside the decoder package, before any of this can
    act. Filed as [issue 21](21-a-truncated-flac-crashes-the-app.md); it happens at HEAD too.
- **Tests.** `UnreadableTracksTests` (17), all through the fake output:
  - passed over with one notice, the row kept and marked;
  - restored and reached again, then played with the mark cleared;
  - A→B authored with B deleted: a hard cut to C, or A→C's own crossfade;
  - a queue change raises no second notice;
  - the held incoming track can be deleted and still plays;
  - ⏭, ⏮ and Transition now pass over a marked row;
  - a double-click that fails leaves the track playing, and one that succeeds clears the mark;
  - Play with nothing readable stays stopped;
  - removing a marked row drops its mark;
  - three decode-error cases on a real FLAC with 4 KB of its middle overwritten: mid-track,
    before the fade-out point, and in the outgoing voice mid-crossfade. No decoder seam
    (ADR 0003);
  - a WAV cut short before its fade-out point.

  The form test covers the mark, the tooltip and the mark clearing. Checked by mutation: with
  the pass-over, the snapshot's walk or the mixer's catch disabled, their tests fail.
