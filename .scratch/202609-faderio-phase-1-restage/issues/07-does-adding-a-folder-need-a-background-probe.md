# Does adding a folder need a background probe?

Type: task
Status: resolved
Blocked by:

## Question

Nothing to decide until it is measured. Adding a folder probes every file's header to issue
its `Length` (ADR 0010), and anything the matrix forces onto a frame scan reads the whole file
at add time. Ticket 14 of the predecessor found no MP3 row needs a scan, and FLAC pre-scans on
open (about 250 ms for the longest library file), not at add time.

Measure the add-time probe over a real library folder: wall time for the largest album folder
and for the whole library (903 files), and the slowest single file. Leave it behind as a
permanent test with a time budget, per the standing rule in `CLAUDE.md`; point it at checked-in
fixtures if the library cannot be a test input, and record the library numbers here.

The answer is the numbers plus a verdict: synchronous add is fine, or folder add needs a
background probe with the queue filling in as files resolve. Agent alone.

## Answer

Measured by the agent alone on 2026-09-23 against `main` at `001e544`, in a Debug build.

**Verdict: folder add needs a background probe.** Adding must also never probe under the
engine lock. That second point is a defect in today's **Add files** as well, not only a
folder-add concern.

### The numbers

`PlaybackEngine.AddTracks` was timed one file at a time (probe plus tag read, the real add path)
over the user's whole music folder: 903 files (654 MP3, 245 FLAC, 4
WAV), none rejected.

| | Pass 1 (cold-ish disk) | Pass 2 (warm cache) |
|---|---|---|
| Whole library, 903 files | **12.8 s** | 1.7 s |
| Per file: median / p95 / max | 12.9 / 26.1 / **112 ms** | 0.7 / 10.9 / 46 ms |
| Largest folder: a compilation, 59 MP3s | 0.85 s | 0.12 s |
| Slowest folder: an album of 18 FLACs | 0.92 s | 0.47 s |
| MP3 total / FLAC total | 8.5 s / 4.3 s | 0.7 s / 1.1 s |

Pass 1 is only "cold-ish": earlier in the session, the first 256 KB of every seventh file had
been read. A truly cold disk after a reboot would be somewhat slower.

**The slow files are cover art, not the probe.** Every one of the five slowest files is on
one album, whose FLACs embed about 7.3 MB of cover art as a PICTURE block. The probe reads
STREAMINFO only. `TrackMetadataReader`'s `TagLib.File.Create` reads every metadata block,
pictures included. No file needed a frame scan at add time.

### Why the verdict

1. **The engine lock.** `AddTracks` runs its probes inside `Run`, under `_sync`, and
   `EngineFrameSource.Read` (the audio thread) takes the same lock. An add during playback
   stalls the audio thread for the whole add. One cold file from it (112 ms) is longer than
   the 100 ms WASAPI-shared buffer and twice the 50 ms exclusive one. A cold album takes 0.85–0.92
   s. This is inferred from the code and the timings; no dropout was recorded. **The probe and
   the tag read must run outside the lock, whatever the UI does.**
2. **The UI thread.** The whole library takes 12.8 s cold. A 391-file discography is about 5.5 s
   (scaled, not measured). A synchronous add from a button freezes the window for that long. An
   album (under 1 s cold) would be tolerable for the UI alone, but point 1 already rules it out.

**What the build must preserve:** probing and tag reading happen off the engine lock and off
the UI thread. The engine appends rows as files resolve, so the queue fills in, in the order
given. Rejections are reported as today (ADR 0007). The command surface (one call per intent,
progress, cancelling a long add, where rows land) is for
[What does queue management add?](12-what-does-queue-management-add.md).

**Optional:** skipping pictures when reading tags would remove the cover-art cost (about 28 ms
per 8 MB, warm). That is worth doing but not required once the add runs off the lock.

### Left behind

`tests/Faderio.Core.Tests/AddTimeBudgetTests.cs`, a permanent test on checked-in fixtures (the
library cannot be a test input):

- all 48 FLAC and MP3 fixtures add in under **1000 ms** (31 ms measured);
- a FLAC with **8 MB of synthetic cover art** (a PICTURE block inserted into a fixture) adds in
  under **150 ms** and keeps its length (28 ms measured, matching that album's warm 30–46 ms).

The budgets catch an order-of-magnitude regression, such as a whole-file scan creeping into the
probe. They do not test the lock. A test for that belongs with the build issue that moves the
probe off it.
