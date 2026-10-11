# Make FLAC seek exact

Type: task
Status: resolved
Blocked by: 09

## Question

Nothing to decide: [ticket 09](09-how-exact-is-the-authoring-promise.md) set the authoring
promise at **1 ms** and chose the mitigation for the one promised format that misses it. Today
FLAC seeks land at the next decoder block boundary, +67 ms on v3's fixture, because
`MediaFoundationReader` never trims after `SetCurrentPosition`. This ticket swaps the decoder
and removes Media Foundation from the production chain. Decoder code only; independent of the
seam rework ([12](12-rework-the-engine-seam.md)) and the matrix fill
([14](14-fill-the-real-input-matrix.md)), so it runs in parallel with either.
Policy in [ADR 0009](../../../docs/adr/0009-authoring-exactness-is-bought-in-the-decoder.md).

### What "done" means

- **`.flac` decodes through `BunLabs.NAudio.Flac` 2.0.1** (MS-PL, netstandard2.0), wrapped by
  the existing `WaveStreamTrackSource`. Red first: tighten
  `Flac_SeekBeforeFirstRead_LandsWithin100ms_PendingTicket09` to a **1 ms** landing assertion,
  renamed without the `_PendingTicket09` suffix, and watch it fail on Media Foundation before
  the swap.
- **Media Foundation is gone from `AudioSources.cs`**: the `_ => MediaFoundationReader` catch-all,
  the `.aif/.aiff` case and the warm-up-read workaround in `WaveStreamTrackSource.Position` are
  deleted. An unknown extension is rejected (the probe itself is ticket 12's; until it lands,
  the factory throws with the rejection reason).
- **The 24-bit and 96 kHz FLAC fixtures decode through the new decoder** with the completeness
  and purity assertions green, as a first check that it handles the promised depths and rates.
  Generate them with `Fixtures/generate.sh`; ticket 14 owns the full twelve-cell grid.
- **Long-file open time measured once**: open the library's longest FLAC (about 26 minutes)
  through the production factory and record the wall time in the Answer. Budget **500 ms**. Over
  budget is not a failure of this ticket; it is a fact for the map, and the agreed next move is
  a background pre-scan at add time.
- **Fallback, only if a matrix cell goes red on the managed decoder and cannot be fixed in a
  vendored copy**: a purpose-built Media Foundation reader on NAudio's public interop
  (`IMFSourceReader`, `IMFSample.GetSampleTime`) that seeks to target minus one maximum block
  and discards by timestamp. Its timestamp accuracy after a FLAC seek is unverified and must be
  measured before it is trusted. Ticket 09's fact report has the details.
- `dotnet test` green, result in the commit message; `docs/supported-audio.md` FLAC row updated
  to name the new landing test.

### Not in this ticket

The probe and structured rejection (12). The FLAC grid beyond the two fixtures above (14).
The engine-side S3 tolerance tightening (12).

## Answer

Resolved 2026-09-23. Built; `dotnet test` green: **Core 170, App 26** (was 165 and 26).

**FLAC seeks land at the exact sample.** `.flac` decodes through `BunLabs.NAudio.Flac` 2.0.1
behind a thin subclass, `FlacTrackReader` (`src/Faderio.Core/Playback/FlacTrackReader.cs`), and
the pinned 100 ms characterisation test is now `Flac_SeekBeforeFirstRead_LandsWithin1ms`. Red
first: tightened to 1 ms it failed on Media Foundation at **+67 ms**, the number ticket 09
predicted; on the managed decoder it passes. The decoder pre-scans every frame header on open,
seeks to the frame *before* the target and decodes-and-discards to the sample: exact by
construction, as ADR 0009 said.

**Media Foundation is gone from the production chain.** `TrackSources.Open` has three cases,
`.wav`, `.mp3`, `.flac`, and throws `NotSupportedException` for anything else (the probe rejects
first). The `.aif/.aiff` case, the `MediaFoundationReader` catch-all and the warm-up-read
workaround in `WaveStreamTrackSource.Position` are deleted; no `MediaFoundation*` symbol remains
in `src/`.

**Two defects in the package, found by the matrix and repaired in the subclass**, not worked
around in the wrapper, so `ITrackSource.Position` stays the decoder's word:

1. **Its position counter skips any read that reaches end of stream.** The `Read` loop returns
   early from inside the frame walk, bypassing `_position += read`. The 96 kHz fixture showed it:
   192000 frames drained, `Position` 189630, and `Flac_ReadsToCompletion`'s "the getter agrees
   with the length it reports" assertion went red. The 44.1 kHz fixtures never hit it because
   88200 is a multiple of the 4410-frame read. `FlacTrackReader` counts frames itself.
2. **A seek into the last block does nothing.** The package's setter walks the pre-scan for the
   first frame starting *at or after* the target; a target inside the last frame matches nothing
   and the seek is silently a no-op, so a voice would have played from wherever it was.
   `FlacTrackReader` seeks to the last frame's first sample through the package's own exact seek
   and decodes forward by hand. New cell: `Flac_SeekIntoLastBlock_LandsWithin1ms` (100 frames
   from the end of `ramp.flac`, inside its 648-frame last block); it was 1998 ms off before.

One trap on the way: the package's setter reads `Position` back *through the virtual getter* to
compute how much to discard, so the subclass answers with the package's counter while a seek is
in progress and with its own otherwise. Also noted, not repaired because nothing uses it:
`FlacMetadataStreamInfo.MinBlockSize`/`MaxBlockSize` are read little-endian (4608 reads as 18);
the decoder seeks by per-frame headers and the add-time probe reads STREAMINFO itself.

**The package's source is not the commit its nuspec names.** The nuspec points at `ff23814e`,
which predates the "Replaced entirely with current CSCore FLAC codec" commit; the DLL's API
(`FlacPreScanMode`, `NAudio.Flac.Metadata`) matches the repository head `ce423833`. Read that
revision when a cell goes red.

**24-bit and 96 kHz decode through the new decoder.** Two fixtures added to
`Fixtures/generate.sh` and checked in: `tone-1k-24bit.flac` (44.1 kHz / 24-bit / stereo) and
`tone-1k-96k.flac` (96 kHz / 16-bit / stereo). `Flac_ReadsToCompletion` and `Flac_ToneIsPure`
are theories over `FlacToneFixtures()`, green on all three. Ticket 14 fills the grid and needs a
ramp fixture per rate for landings.

**Long-file open time: within budget.** The library's longest FLAC is one
track of 25.98 min, 123.6 MB, 68.7 M frames (found by probing every file's duration; the largest
files by size are 23-minute album tracks). Through the production factory:

| build | open (pre-scan) | seek to last minute + one read |
|---|---|---|
| Debug | 241-256 ms | 1-25 ms |
| Release | 193-245 ms | 1-28 ms |

Budget 500 ms: met, so the pre-scan stays synchronous on open and no background pre-scan is
needed. One outlier for the record: the first-ever open of a 23-minute file in this session
took 8.7 s, unreproducible afterwards on any file cold or warm (a cold sequential read of a
sibling 140 MB file took 213 ms, so not the disk). Most likely the antivirus first-touch scan
or the drive waking. The smoke checklist's long-file item is the place it would show up again.
The add-time probe on the same file: under 5 ms.

**Docs.** `docs/supported-audio.md` FLAC row names the four tests; the decoders paragraph
names `FlacTrackReader` and its two repairs. Ticket 14 carries a "Noted from ticket 15" with
the fixture names and the two package facts. No ADR change: ADR 0009's decision stands as made.

## Noted from ticket 12

`AudioSources.cs` is now `TrackSources.Open(string path)` (internal static) with an explicit
`.flac` case that still routes to `MediaFoundationReader`, plus the `_ =>` catch-all and the
`.aif/.aiff` case, all three of which this ticket deletes. The warm-up read in
`WaveStreamTrackSource.Position` is unchanged and goes with them. The probe (`AudioProbe.ProbeFlac`)
reads STREAMINFO itself, so the decoder swap does not touch add-time behaviour. The matrix's
`Flac_SeekBeforeFirstRead_LandsWithin100ms_PendingTicket09` is the pinned test to tighten to
1 ms and rename.
