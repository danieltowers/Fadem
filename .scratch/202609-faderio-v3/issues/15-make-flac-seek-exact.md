# Make FLAC seek exact

Type: task
Status: open
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

_(pending)_
