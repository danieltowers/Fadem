# 10: Transitions never wait on the disk

Status: resolved
Blocked by: none

Phase 6, improvements round 1, both hosts (engine only). Raised 2026-10-03: "choppy transition
sometimes, mainly when controlling a library over network, but sometimes even when running
locally."

## The cause (read from the code, not yet measured)

- The incoming track is opened ahead of time: `ScheduleBoundary`
  (`PlaybackEngineBoundary.cs`) opens it when the outgoing track starts and keeps it as
  `_prepared`. Nothing reads audio ahead of time.
- The crossfade fires from a scheduled event inside `TransitionMixer.Read`, on the render
  thread, holding the engine lock (`TryEnterToRender`). `Crossfade` → `StartTrack` →
  `ScheduleBoundary` then opens the **track after next** right there, as the fade begins.
- An open reads through the whole file. FLAC is opened with `FlacPreScanMode.Sync` (every frame
  header). NAudio's `Mp3FileReader` builds its table of contents from every frame.
  `docs/supported-audio.md` records about 250 ms for a 26-minute FLAC on a local disk. Over the
  network it can take seconds.
- Device buffers are 50 ms (exclusive), 100 ms (shared) and 150 ms (WaveOut), so any open
  longer than that is heard as a dropout at the start of the fade.
- During the fade both voices decode straight from their files on the render thread. A slow
  network read while two files stream is a second, smaller source of glitches.
- Commands that open a file under the engine lock (a skip, Transition now, a queue edit that
  reschedules the boundary) also stall rendering while the open runs.

## What to build

Two changes, built together:

1. **Open off the render thread.** The boundary's incoming track is opened by a background
   task. The render path never opens a file and never waits on one. It only takes the result
   once the task has finished. A command that reschedules the boundary (a seek, a queue edit)
   only starts the task. A command that needs a track now still opens it itself (decided
   2026-10-03, see Comments).
2. **Hold the next track in memory.** Before its boundary, the incoming track's file bytes are
   read into memory in full, and its decoder is opened over that memory. The crossfade, and the
   whole of that track, then never touches the network or the disk.

## Design (decided 2026-10-03)

- **Not ready in time.** The outgoing track carries on at full volume, and the transition
  begins as soon as the incoming track is ready. Its outgoing fade is shortened to what is left
  of the outgoing track, and the fade-in shortens in the same proportion. If the outgoing track ends first,
  there is silence until the incoming track is ready, then it starts, its side as authored.
  Rendering never waits, so that silence cannot be avoided. One notice either way.
- **When to start the read.** As soon as the boundary is scheduled, i.e. when the
  outgoing track starts, which is when the file is opened today. That leaves the whole length of
  the outgoing track to finish the read.
- **Memory cap.** The MusicBee plugin is net48 x86 (ADR 0012), in a 32-bit process shared with
  MusicBee. Files up to 256 MB (64 MB in a 32-bit process) are read into memory, in 64 KB chunks. Above the cap, or with
  no memory to be had, the file is still opened in the background, but read from disk as today. Only the incoming track is
  held, so at most two files are in memory at once (the playing one and the next).
  Compressed bytes, not decoded samples: a decoded 26-minute 96 kHz stereo track is over 1 GB.
- **Unreadable files.** A read or open that fails in the background marks the row unreadable and
  passes over it with a notice, exactly as `OpenFirstReadable` does today, before the boundary
  is reached.
- **Queue edits during the read.** A change that reschedules the boundary cancels the read in
  flight and disposes its result, as `DropPrepared` does now.
- **Exactness holds.** Authored fade points stay sample-exact (ADR 0001, ADR 0009). Only *when*
  the file is read changes. *Where* the fade lands stays the same. The full scan stays.
- **Preview** (`_previewMixer`) opens both its tracks in its command, never while rendering,
  so it is unchanged.
- Out of scope: a read-ahead buffer per voice for the track that is already playing (option 3 of
  the 2026-10-03 advice), and opening without the full scan (option 4).

## Acceptance

- [x] No file is opened on the render thread: a test fails if one is opened during a render.
  A prepared track is not read from disk there either. *Not met in full:* a track a command
  opened (the first one played, a row, a skip target not yet prepared) and a file over the cap
  are still read from disk as they play, as before.
- [x] A permanent integration test with a track source that blocks on open for longer than the
  device buffer: the transition renders with no gap, and the fade lands where it is authored.
- [x] The not-ready fallback is covered by a test: no silence, and the behaviour decided above.
- [x] Files above the cap still play and still transition.
- [x] A row whose file disappears before its boundary is passed over with a notice, as today.
- [x] `dotnet test` passes on net8.0 and net48 x86.
- [ ] The user confirms transitions are smooth over the network library (Sounds, the user's).

## Comments

Built 2026-10-03. Recorded as [ADR 0015](../../../docs/adr/0015-rendering-never-opens-a-file.md).

- **Engine.** `PlaybackEnginePrepare.cs` holds it all. `ScheduleBoundary` picks the next row
  that may be readable and starts a worker thread ("faderio prepare") on its file. The worker
  reads the file into a `MemoryFile` and opens the decoder over it, off the lock, then answers
  under it. Rendering takes the track only if it is there (`TakePrepared`).
- **Late.** At the fade-out point with the track still being read, the boundary is marked late
  and the outgoing voice plays on. When the worker answers, the transition begins there.
- **Awaited.** At the outgoing voice's end with the track still being read, the ended voice
  stays current and in the view, as the last track's tail does, and the queue is silent until
  the worker answers. Transition now and Cue transition are off meanwhile.
- **Failure.** A file the worker cannot open is marked and passed over with the notice it
  always had, and the boundary is rescheduled into the row after it.
- **Changed from the proposal.** A late fade is shortened to what is left of the outgoing
  track, where the proposal dropped the fade unless all of it still fitted. A fade authored to
  run to the track's end, as the default crossfade does, would otherwise lose its whole fade
  to any lateness at all.
- **Command scope** (the user's call, 2026-10-03): only the boundary's open moved. Play, a row,
  a skip to a row not yet prepared, Previous and Preview still open under the lock. These are
  hard cuts, and a skip's usual target is already prepared. Moving them is a follow-up.
- **Test seams** (the user's call): the engine's surface on the fake output, with three
  internal hooks beside `StartAddWorker`: `StartPrepareWorker` (a test holds an open back and
  runs it when it chooses), `TrackFileOpening` (a test fails if a file is opened during a
  render) and `PreparedMemoryCap`. `PreparedInTheBackgroundTests` is the permanent test.
- The renderer and every test engine run the worker inline: a file is written, and the fake
  clock runs, faster than a background thread could keep up with.
- **Memory.** Up to three files are held during a transition (its two tracks and the one
  prepared after them), not two as first written. So the cap is 64 MB in the plugin's 32-bit
  process and 256 MB otherwise.
- **Review** (standards and spec, 2026-10-03) found, and this fixes: an endless retry under
  repeat-all when no file could be opened (each row is now tried once per search); a late
  boundary that ignored an edit to its own transition; a late fade whose fade-in kept its full
  length while the fade-out shortened, an audible dip; a worker fault that left the queue
  waiting for ever.
- **Known limits.** Dropping a preparation stops its read between chunks, but not a decoder
  already scanning. The old re-check of the incoming file at the boundary is gone: the file is
  opened once, when the boundary is scheduled. Running out of memory while the decoder opens,
  as opposed to while the file is read, still marks the row unreadable. The over-cap test is a
  hard cut. No test blocks a real thread inside an open while rendering: the tests hold the
  open back instead, by the seam agreed.
- **Left for the user:** the last acceptance item, by ear, now also in the Sounds half of
  `docs/smoke.md`.
