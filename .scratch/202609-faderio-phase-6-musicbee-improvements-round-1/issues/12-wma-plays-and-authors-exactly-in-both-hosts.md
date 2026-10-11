# 12: WMA plays and authors exactly, in both hosts

Status: ready-for-human
Blocked by: none

Phase 6, improvements round 1, both hosts (engine; the shell only names the format). Raised
2026-10-05: "we certainly need WMA", and "it needs to work for standalone as well as MusicBee".

WMA is rejected today ([ADR 0007](../../../docs/adr/0007-audio-is-promised-or-rejected.md),
`docs/supported-audio.md`). The reason was the decoder. In ticket 02, Media Foundation, the only
WMA decoder then in reach, missed seeks by up to 71 ms and reported the wrong length. This
issue makes WMA a promised row, proven like WAV, FLAC and MP3, through **one decoder that both
the standalone app and the MusicBee plugin use**.

## What the research found

Research on 2026-10-05, in [`docs/research/wma-through-musicbee-bass.md`](../../../docs/research/wma-through-musicbee-bass.md).
The findings are pinned by `tests/Faderio.Core.Tests/MusicBeeBassWmaTests.cs` (commit
`f17bf22`). Measured on MusicBee's own `bass.dll` 2.4.17 and `basswma.dll` 2.4.5, over WMA 9
Standard files from Windows' own encoder (what Windows Media Player rips) and from ffmpeg:

- **basswma decodes through Windows.** It is a thin wrapper over the Windows Media Format
  runtime (`wmvcore`), the older Windows route to WMA. It is not Media Foundation, so ticket
  02's numbers do not apply to it.
- **A plain seek is not exact.** BASS lands 0.1 to 1.6 ms late on Windows-encoded files (15 of
  75 seeks missed 1 ms) and about 45 ms early on ffmpeg's.
- **Decoding forward from the start is exact.** `BASS_POS_DECODETO` from a fresh stream landed
  on the sample at every target on every file. The decode starts at the source's first sample,
  so there is no encoder delay to trim.
- **Decoding forward costs time.** WMA decodes at about 265× real time. A fade-in point 10 s
  in takes about 40 ms to reach. The last minute of a 26-minute track takes about 6 s.
- **The header's length is not exact.** Windows' files end in 17 to 43 ms of codec tail, which
  is decoded and counted. The header gives the length in milliseconds (16 frames short at
  44.1 kHz). ffmpeg's files are a partial block shorter than their source, and their header
  disagrees with what decodes.
- **Tone purity** is at least 0.999 on every file.

## Why not MusicBee's BASS

BASS answered the research question, but it cannot be the decoder for this issue:

- **Standalone has no BASS.** The DLLs come with MusicBee. Shipping BASS with faderio means
  Un4seen's licence (free only for non-commercial use) and a 32-bit and a 64-bit copy.
- **Two decoders, one per host, would split the promise.** One host could pass the matrix while
  the other fails. Decoders are not a port
  ([ADR 0003](../../../docs/adr/0003-command-surface-and-view.md), as `TrackSources` cites it), so one adapter would be
  the whole design.
- **BASS adds nothing to the decode.** basswma hands WMA to the same Windows runtime. faderio can
  call that runtime itself, in either process, with no new dependency.

## What to build

1. **A WMA reader over the Windows Media Format runtime**, in Core, used by both hosts.
   `WmaTrackReader` opens a file with `WMCreateSyncReader`, asks for PCM output, and is an
   `ITrackSource` like the others, dispatched by extension in `TrackSources.Open`. It runs on
   net8.0 x64 (standalone) and net48 x86 (the plugin) alike. The runtime ships with Windows on
   both bitnesses.
2. **Exact seeks** by decoding forward and trimming to the sample. How far back to start is
   the first open question below.
3. **A probe for WMA** in `AudioProbe` and a row in `SupportedAudio`. The probe reads the ASF
   header for rate, channels and codec, and gives the length decided below.
4. **A row in `docs/supported-audio.md`** for each WMA cell that passes the five matrix theories
   (`Wma_ReadsToCompletion`, `Wma_ToneIsPure`, `Wma_HeaderLengthEqualsDecoded`,
   `Wma_SeekBeforeFirstRead_LandsWithin1ms`, `Wma_SeekIntoLastBlock_LandsWithin1ms`). The tests
   run on net8.0 and on net48 x86. The fixtures are already checked in (`*-windows-*.wma` and
   `*-ffmpeg-*.wma`, from `Fixtures/generate.sh`).
5. **The hosts accept WMA.** Folder adds take `.wma` (`SupportedAudio.IsPromisedExtension`). The
   standalone open dialog's filter names it (`MainForm.cs`). The plugin needs no change: it is
   handed file paths.
6. **ADR 0007's consequences and ADR 0009's "M4A and WMA have no way back" are amended**, or
   a new ADR records how WMA came in.

`MusicBeeBass.cs` and `MusicBeeBassWmaTests.cs` stay as the research's record, or are deleted
once the new reader's matrix cells pass. That is a decision for the build.

## Open questions (to settle before building)

1. **Seeking deep into a long file.** Decoding from the start is proven exact but costs about
   6 s at the end of a 26-minute track. That is fine for a boundary's incoming track, which a
   worker opens ahead of time ([ADR 0015](../../../docs/adr/0015-rendering-never-opens-a-file.md)).
   It is not fine for a seek the user makes while listening, which runs under the engine lock.
   The candidate answer is to seek with the runtime's own `SetRange` to a little before the
   target, then decode forward and trim by each sample's timestamp. That is ADR 0009's fallback
   for FLAC, never built. It is exact only if the runtime's sample timestamps are accurate after
   a seek. BASS's plain seek landing late by up to 1.6 ms suggests they may not be to the
   sample: ASF timestamps are in milliseconds. **First task: measure it**, as a permanent test
   on the existing fixtures. If the timestamps are not exact, the choices are decoding from the
   start (with the user's seek moved off the lock), or a coarse table of sample positions built
   at open by one full decode.
2. **What the length is.** ADR 0010 needs the add-time length to equal the decoded length,
   exactly. The ASF header gives milliseconds, and the decode includes the codec tail. Options:
   decode the whole file at add time to count it (about 6 s for 26 minutes, in the background add
   worker); take the header's length and let ADR 0010's one notice cover the gap; or trim the
   tail if the file records it. Whether the tail counts is the same kind of question ADR 0010's
   addendum answered for MP3.
3. **ffmpeg-made WMA.** These files hold a partial block less than their source, and their
   header disagrees with their decode. If the length comes from the decode, they pass like
   Windows' files. If it comes from the header, they need their own rule or a rejection. The
   probe cannot tell the two encoders apart from the codec alone.
4. **Which WMA codecs.** Only WMA 9 Standard (`wmav2`) was measured. WMA Pro, WMA Lossless and
   WMA Voice also come out of Windows Media Player and the same runtime. Promise Standard only
   and reject the rest by codec at add time, or measure them too? Windows' encoder can write
   Pro and Lossless (`BASS_WMA_ENCODE_PRO` and lossless flags in `encode-wma.ps1`), so their
   fixtures cost one line each.
5. **Rates and channels.** Windows' encoder offers no mono at 48 kHz, so that cell has only an
   ffmpeg fixture. Promise mono 48 kHz on ffmpeg's evidence, or leave it out?
6. **Windows N editions.** The Windows Media Format runtime is missing from N and KN editions
   without the Media Feature Pack. MusicBee's basswma needs it too. A WMA file on such a
   machine should be rejected with that reason when it is added, not fail when it plays.

## Out of scope

- M4A / AAC. It is blocked the same way (Media Foundation, 15 ms in ticket 02). If question 1's
  timestamp trim works for WMA, the same approach may work for M4A through Media Foundation.
  That is a separate issue.
- Ogg, Opus, APE and WavPack.

## Acceptance

- [x] WMA 9 Standard files from Windows' encoder, at 44.1 and 48 kHz, mono and stereo where the
  encoder offers them, pass all five matrix theories on net8.0 and net48 x86, through
  `TrackSources.Open`.
- [x] A seek anywhere in the file lands within 1 ms. A seek into the last minute of a
  26-minute WMA completes within the open budget (500 ms), or the design settled in question 1
  says why it need not.
- [x] The add-time probe accepts the promised cells and rejects the rest (unpromised codecs,
  rates and channels; a missing Windows Media Format runtime) with the reason.
- [x] `docs/supported-audio.md` has the WMA rows, each naming its theories. The rejected table
  no longer lists WMA, or lists only what is still rejected.
- [ ] Adding a folder in standalone and dropping tracks in MusicBee both queue `.wma` files.
  (Standalone checked; MusicBee left to the user.)
- [x] `dotnet test` passes on net8.0 and net48 x86 (bar one MusicBee panel test that fails on
  HEAD before this change, and a FLAC timing flake; see the commit).
- [ ] Looks: a WMA shows its length and seeks in both hosts (`docs/smoke.md`). Standalone
  passed; MusicBee not run.
- [ ] Sounds (the user's): a transition into and out of a WMA lands where it was authored.

## Comments

### 2026-10-05, the build

Question 1 measured (`WmaRuntimeTests`): stamps are whole milliseconds and a seek lands up to 2 ms
from them, but `SetRange` costs 1 to 17 ms even 26 minutes in, and what a seek delivers is the
decode from the start, cut at the same boundaries. The user chose a boundary index: one full
decode at add time, kept in `<library>\.faderio\wma-index\` and keyed by the file's last
mebibyte so tag edits keep it; seeks are then exact in milliseconds. Questions 2 and 3: length is
the decode, codec tail included. Questions 4 and 5: WMA 9 Standard only, all four cells; Pro is
rejected by codec (fixture `tone-windowspro-44100-2.wma`). Question 6: rejected at add time with
the reason. Recorded in ADR 0016. A file with no index (another machine, or changed audio) is
decoded in full when opened, under the lock for a direct play: about 0.75 s for 4 minutes, 5 s
for 26. Left: the MusicBee Looks items and the Sounds half.


### 2026-10-05, WMA 9 Lossless

Measured after the user's Lossless files were rejected, and promised: stereo, 44.1 kHz at 16 or
24-bit, 48 and 96 kHz at 24-bit, every cell Windows' encoder writes. The reader now asks the
runtime for the file's own rate and depth (its default turned 24-bit into 16 and 96 kHz into
48), and asks earlier when a Lossless seek lands past its target (up to 0.4 s). The decode is the
source, sample for sample. ADR 0016's addendum. The user checked it in MusicBee: it works.
