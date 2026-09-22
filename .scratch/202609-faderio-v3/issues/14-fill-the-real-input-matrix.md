# Fill the real-input matrix

Type: task
Status: open
Blocked by: 07

## Question

Nothing to decide: build seam S2 from
[ticket 06](06-how-do-we-make-green-tests-and-a-broken-app-impossible.md) — real files through
the production decoder chain, three assertions per cell. Ticket 07 lands the skeleton; this
ticket fills it. Independent of the seam rework (12) and the shell (13), so it can run in
parallel with either.

The FFT and cross-correlation harnesses from tickets 01 and 02 do not exist as code. Their
methods are fully documented in those tickets' answers; this is where they are rebuilt, as
permanent tests, per the map's research rule.

### What "done" means

- **Cells**: WAV and FLAC × 16/24-bit × 44.1/48/96 kHz × mono/stereo. WAV generated at test
  time by `WaveFileWriter`; FLAC **checked in**, with the generating ffmpeg script beside the
  fixtures. Shaped so that a format [ticket 08](08-what-audio-does-faderio-promise-to-play-correctly.md)
  adds later is one row.
- **Three assertions on every cell**, through the production chain:
  1. Completeness — rendered frame count equals decoded length.
  2. Purity — ≥ 0.999 of spectral energy in band (ticket 01's method).
  3. Seek round-trip — pink noise, seek to a known offset, recover the landing point by
     cross-correlation (ticket 02's method).
- **FLAC seek is a pinned characterisation test** at ≤ 40 ms, named
  `Flac_SeekLandsWithin40ms_PendingTicket09`. Green, documented, catches a regression to
  2267 ms.
- Defects A and B from ticket 01 are provably fixed: the 48 kHz and 96 kHz cells complete,
  the 24-bit cells are not silent.
- No cell skips. A missing fixture is a failure.

## Answer

_(pending)_
