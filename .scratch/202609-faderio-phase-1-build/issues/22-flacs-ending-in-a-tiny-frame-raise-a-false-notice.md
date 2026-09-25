# 22: FLACs ending in a tiny frame raise a false notice

Status: resolved
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

## Comments

- Built 2026-09-24. **Decided: repair both in `FlacTrackReader`, as its fourth and fifth package
  defects.** The package is not vendored.
- **A final frame under 16 bytes.** The cause above was right. The reader's `EndingStream` now
  reads 16 bytes of zeros past its end. The pre-scan then finds a header in the file's last
  16 bytes, and the decoder's 16-byte header read succeeds on a short final frame. A frame's body
  is read only to the stream's `Length`, so no frame decodes a zero. Frames are counted by the
  pre-scan itself, not added after the last one it found, so the length, the seek table and the
  CRC check all see the short frame. Side effect, tested: a truncated file cut just after a
  frame header (header whole) now loses only that frame, not the one before as well. A cut
  inside the header still costs both.
- **The 1995 track's case: a false frame header, and it did need something.** Read in the file:
  the pre-scan found bytes inside frame 1561 that parse as a header of the same format with a
  matching CRC-8 (frame number 9, 256 samples). The package counts it as a frame, so:
  - the length is 256 samples long;
  - every seek after it (from 145 s on) lands 256 samples late;
  - a seek into the next frame lands *on* the phantom and decodes it. In the fixture this
    killed the child with an unhandled exception.

  So the answer to "what does it do to an exact seek near the end" is: it makes it late. The whole
  music folder has 5 such files (256 to 4096 samples each, 3 of them also among the 110). One of
  those hid it completely: its lost 4096-sample final frame cancelled out a 4096-sample phantom.
- **Repair:** `RealFrames` drops a scanned frame whose number is out of step with the frame
  before it, when the frame after it is back in step (or it is the last). Frames are numbered
  by frame or by sample, and either step is accepted because the package reads a frame number
  as a sample number whenever STREAMINFO's block sizes differ. A real jump in numbering is kept.
  The reader's length, sums and CRC runs go by the real frames. Seeks ask the package for the frame
  before the target, one sample in, by the package's own offset for that frame. That can never
  be a phantom, and it is never the package's last frame, so defect 2 needs no special case now.
  Reads stop at the end of the whole audio. With two or more phantoms, the package's frame
  counter would otherwise jump back and replay a frame at the end.
- **Fixtures (the real-input question): yes, two**, from `generate.sh`, outside the promised grid:
  `silent-tail-44100-16-2.flac` (noise, then silence to whole blocks, ending in 14-byte frames)
  and `false-header-44100-16-1.flac` (verbatim white noise with a valid header of the same format
  written into the PCM). `FlacFrameTableTests` (11): each fixture first shows it reproduces the
  defect in the package's own table, then opens to its header's length, decodes in full, and
  seeks exactly (compared with a sequential decode, in a child process). The silent tail also
  plays through the engine with no notice, and the next track starts where it ends. Against
  the old reader 7 of 11 fail, one of them by the child dying.
- **Music folder, opt-in** (`FADERIO_MUSIC_FOLDER`): the test now checks every promised FLAC
  against its header, not the package's pre-scan. Each file must open to its header's length
  and decode to it, and a seek 1.5 s before the end must read what a sequential decode has there.
  It decodes every file in full, so the whole folder is too slow to run: it had not finished
  after hours. Run instead on 50 of the 245, hard-linked into one folder: all 5 with a phantom
  frame, then 45 spread evenly over the rest (21 ending in a short frame, 24 with neither). It
  passed on all 50 in 3.5 minutes.
