# 21: A truncated FLAC crashes the app

Status: resolved
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

## Comments

- Built 2026-09-24. Reproduced first, in a child process: the fixture cut in half exited
  `0xC0000005` (-1073741819) with the access violation in `FlacBitReader.ReadUnary`, and the test
  host lived.
- **The cause, read in the package's source (decompiled):** `FlacFrame.ReadSubFrames` copies
  whatever is left of the stream, at most 128 KB, into a fresh zero-filled buffer and decodes it
  through a pointer with no bounds. Past a cut the buffer is zeros, and a Rice code's unary run
  (`while (num2 == 8)` over whole zero bytes) counts on off the end of the buffer. The suspected
  cause was right.
- **Decided: guard it in `FlacTrackReader`, by CRC, not by STREAMINFO.** Every FLAC frame ends
  with a CRC-16 of its bytes. Walking back from the last frame the pre-scan found, the first
  whose bytes (to the next scanned frame, or to where the audio ends: the end of the file less
  any ID3v1 and APEv2 tags) end with a matching CRC ends the whole audio. The reader hands the package a stream that ends there
  (`EndingStream`), so no cut frame reaches the decoder: at that end it finds no frame header and
  stops. `Length` shrinks to match. That covers what the STREAMINFO comparison proposed here
  would not: a cut inside the true final frame, a tail of zeros where a copy never reached (the
  file already at full size), and a STREAMINFO sample count of 0 ("unknown"). A cut frame passes
  by chance 1 time in 65536. The CRC is zero again after each whole frame, so whole frames the
  pre-scan missed after the last it found still pass together. No run longer than the longest
  possible frame plus 16 bytes is read (STREAMINFO's maximum frame size is not trusted: a run can
  exceed it and a header can understate it). ADR 0009 gains an addendum; the package is not
  vendored.
- **Behaviour:** a cut file plays to the end of its last whole frame and the boundary fires
  there, with ADR 0010's one notice ("… ends N s before its header claims."). A file cut inside
  its first frame fails to open, so it is Unreadable: passed over, row marked. A cut inside a
  frame's first 16 bytes also loses the whole frame before it, because the package's pre-scan
  never looks at a file's last 16 bytes. A whole last frame followed by anything but whole frames
  and those tags (Lyrics3, padding) is refused, and costs a notice and one frame; at HEAD such a
  file played in full. A false frame header inside a whole file's final frame (the kind issue 22
  saw once) would cost that frame the same way.
- **Tests:** `TruncatedFlacTests` (36). `ChildProcess` makes the test assembly its own child
  (`GenerateProgramFile` off, `Main` dispatches `decode` and `play`), so every decode of a cut
  file runs out of process. Covered: all 24 FLAC fixtures cut inside a middle frame; a cut inside
  the final frame; on a frame boundary (nothing lost); inside a frame header (one frame lost); a
  zero tail; inside the first frame (a managed error on open); a seek into the last whole frame
  (exact) and into the dropped one (stops at the last whole sample); whole files, in process,
  with an ID3v1 tag and with an APEv2 tag then ID3v1 (decode in full) and with 100 other bytes
  after the last frame (loses it, the documented cost); and through the engine: one notice, and
  the next track starts where the whole audio ends. Against the old reader 32 of 36 fail, 29 of
  them by access violation in the child; the frame-boundary and both tag tests pass there too,
  as they should, and so does the opt-in test, which looks at nothing without its variable.
- **Music folder, opt-in** (`FADERIO_MUSIC_FOLDER`): all 245 promised FLACs in the author's `MUSIC` folder
  open to exactly the length of the package's own pre-scan, so the check takes no frame from a
  whole file. The same run found that 110 of them open one block short of their header: filed as
  [issue 22](22-flacs-ending-in-a-tiny-frame-raise-a-false-notice.md), not caused by this.
- **Not covered:** a frame damaged in place, not cut, with a run of zeros longer than the rest of
  the 128 KB buffer, could still crash the decoder. None has been seen; the noise-damaged file of
  issue 10 throws a managed exception. `CONTEXT.md` names "truncated" under **Unreadable**, but
  a truncated file with whole frames plays short with a notice, as ADR 0010 says and as a
  truncated WAV already did; only a file with nothing whole is passed over. The glossary is
  unchanged: a gap for `/domain-modeling`, not decided here.
- Glossary gap closed 2026-09-26 (`/domain-modeling`): **Unreadable** now means a file that
  cannot be opened, down to one damaged so that none of it plays. A file cut short that still
  plays is not Unreadable: it ends where its audio ends, with ADR 0010's notice. The code
  already did this; only `CONTEXT.md` changed.
- Code review (standards and spec, in parallel): no hard violations. Fixed from it: the size
  bound (was STREAMINFO's maximum frame size, applied to a run that can hold more than one
  frame), APEv2 tags, a child-process timeout that could not fire (stdout was read to the end
  before the wait), a read loop duplicated in two files (now `TestAudio.ReadToEnd`), the name
  `WholeFrames` (now `WholeFlacFrames`: "frames" means audio frames elsewhere), and "library"
  for a folder on disk (the glossary's **Library** is the player's).
