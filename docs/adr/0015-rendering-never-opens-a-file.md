# Rendering never opens a file; the next track is opened in the background and held in memory

Status: accepted (2026-10-03)

Transitions were choppy, mostly on a library over the network. Opening a track reads through
the whole file (ADR 0009: the FLAC pre-scan, the MP3 table of contents), about 250 ms for a long
local FLAC and seconds over a network. The engine opened the track after next on the render
thread, under the engine lock, at the instant a fade began. The device buffer is 50 to 150 ms.

The incoming track of a boundary is now opened by a worker thread as soon as the boundary is
scheduled, and its file's bytes are read whole into memory first. Rendering takes the track only
once it is there, and never waits for it.

Decided and built in
[phase 6 issue 10](../../.scratch/202609-faderio-phase-6-musicbee-improvements-round-1/issues/10-transitions-never-wait-on-the-disk.md).

## What happens when the track is not there in time

Rendering does not wait, so the boundary cannot always land where it is authored:

- **At the fade-out point:** the outgoing track plays on at full volume. The transition begins
  when the track arrives, as authored except that the outgoing fade is no longer than what is
  left of its track, and the fade-in shortens in the same proportion. One notice says how late.
- **At the outgoing track's end:** silence until the track arrives, then it starts, its side as
  authored. One notice says how long the silence was. The ended track stays in the view
  meanwhile, as the last track's tail does.
- **A track that cannot be opened** is marked and passed over with its notice when the worker
  finds out, usually long before the boundary, and the boundary goes to the next row with that
  adjacency's own transition.

A track that arrives in time changes no sample: ADR 0001 and ADR 0009 hold as before.

## Considered options

**A read-ahead buffer per voice.** A decode thread stays ahead of the mixer for every voice, so
the playing track never waits on the disk either. Deferred: it is the larger change, and every
exact-timing guarantee would have to be proven through it. Holding the next track's file in
memory gets the transition, and the whole of that track, off the disk without it.

**Open without the full scan.** Rejected here: the scan is what ADR 0009 buys exact seeks with.

**Hold decoded samples.** Rejected: a decoded 26-minute 96 kHz track is over 1 GB. The file's own
bytes are held, in 64 KB chunks, because the plugin's process is 32-bit (ADR 0012) and may have
no contiguous run the size of a file. Files over 256 MB (64 MB in a 32-bit process), and any
there is no memory for, are still opened in the background but read from disk as they play.

## Consequences

- Up to three files are in memory, during a transition: its outgoing and incoming tracks, and
  the one prepared after them, whose read starts as the fade does. Outside one, two. Hence the
  lower cap in the plugin's 32-bit process.
- Dropping a preparation stops its read between chunks. A decoder already scanning, or a file
  over the cap, runs to the end of its open and is closed then.
- A command that needs a track now (Play, a row, a skip to a row not yet prepared, Previous,
  Preview) still opens it under the lock, as before. These are hard cuts. Moving them off the
  lock is a separate change.
- The renderer and the tests run the worker inline: a file is written, and a fake clock runs,
  faster than any background could keep up with.
