# 22: FLACs ending in a tiny frame raise a false notice

Status: needs-triage
Blocked by: —

Found while building [issue 21](21-a-truncated-flac-crashes-the-app.md) on 2026-09-24. Not
caused by it: the HEAD build before issue 21 (e6be23d) raises the same notice.

## What happened

`TruncatedFlacTests.EveryFlacInAMusicFolder_OpensToThePackagesOwnLength`, first written to compare
against the header, was run over the author's `MUSIC` folder (245 promised FLACs):

- 110 files open exactly one block short of their STREAMINFO sample count: 4096 samples in the
  rips of two artists, 4608 in a third artist's album. Decoding one of them
  (track 3 of a 1969 album) stopped at the same sample.
- One file, track 4 of a 1995 album, opens 256 samples **over** its
  header (header 16825032, pre-scan 16825288).
- Played through the test engine in a child process,
  track 6 of a 1970 album raised the notice
  `"06 - ….flac" ends 0.1 s before its header claims.` and the next track started 93 ms
  early. Same result on the HEAD reader.

So about 45% of these files raise a false ADR 0010 notice at every play, on whole files.

## Cause (read in the package's source, and seen in one file)

Track 3 of the 1969 album ends in two 15-byte frames (digital silence: constant subframes).
The package loses the last one twice over:

- The pre-scan (`FlacPreScan.ScanThisShit`) never looks for a frame header in a file's last
  16 bytes, so its table stops one frame short.
- The decoder's frame-header read (`FlacFrameHeader.ParseHeader(Stream, …)`) reads 16 bytes and
  fails if it gets fewer, so decoding stops one frame short as well.

The 1995 track's excess is probably a false frame header in the pre-scan (a sync code and a
CRC-8 that match by chance). Not investigated.

## Why it matters

ADR 0010 promises that the header's length equals what decodes, "never silent, never per
block", and that the notice means the file is short. Here it cries wolf on whole files, and a
real truncation looks the same as a CD rip's silent last frame.

## To decide

- Repair it in `FlacTrackReader` as the fourth package defect: let the frame-header read see 16
  bytes past a tiny final frame (padding past the end, which a whole frame never reads), and
  count the frames the pre-scan missed after the last it found. Issue 21's CRC check already
  accepts those frames as whole.
- Whether the real-input matrix gains a fixture that ends in a frame under 16 bytes, so this is
  proven in the suite rather than only on the author's music folder.
- Whether the 1995 track's case (pre-scan longer than the header) needs anything, and what it does
  to an exact seek near the end of that file.
