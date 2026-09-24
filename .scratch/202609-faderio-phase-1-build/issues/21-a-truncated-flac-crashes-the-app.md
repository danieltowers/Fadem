# 21: A truncated FLAC crashes the app

Status: needs-triage
Blocked by: —

Found while building [issue 10](10-unreadable-tracks-are-passed-over.md) on 2026-09-24. Not
caused by it: the HEAD build before issue 10 (d515140) crashes the same way.

## What happened

`tests/Faderio.Core.Tests/Fixtures/noise-44100-16-2.flac` was copied with its second half cut
off. The copy was opened through `TrackSources.Open` and read in 4410-frame blocks:

- The open succeeds. The pre-scan does not notice that the file is short.
- 39690 frames read cleanly.
- The next read kills the process with `System.AccessViolationException: Attempted to read or
  write protected memory`. .NET cannot catch this, so neither issue 10's per-voice catch nor
  anything else can turn it into a notice. The test host died.

The same copy with 4 KB of its middle overwritten by noise, rather than cut, throws a managed
`FlacException` ("Not supported RICE-Coding-Method") at the same place. Issue 10 handles that
case, and its decode-error tests use this damaged-but-whole file for that reason. Damaged
MP3s (cut or overwritten) do not throw at all: they decode short or decode noise.

## Why it matters

"Truncated" is one of the causes named in **Unreadable** (`CONTEXT.md`). A FLAC that was only
partly copied, or that sits on a drive that dropped mid-copy, would be probed and queued (the
probe reads only the header, ADR 0010), then crash the player when it plays.

## Suspected cause (from the symptom, not read in the package's source)

`BunLabs.NAudio.Flac` decodes with unsafe code and reads past the end of its buffer when the
last frame the pre-scan found is incomplete.

## To decide

- Guard it in `FlacTrackReader`, which already repairs two package defects (ticket 15). For
  example: when the pre-scan's frames cover fewer samples than STREAMINFO claims, the file is
  short, so stop before the last frame found. That frame may be cut, and the shortfall becomes
  ADR 0010's one notice. A cut inside the true final frame would still get through.
- Or fix it in the package, or replace the decoder (ADR 0009 chose it for exact seeks).
- A regression test must run the crash in a child process, since it takes down the test host.
