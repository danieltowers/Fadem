# Trim tagged MP3s, or keep whole frames?

Type: grilling
Status: resolved
Blocked by: 08

## Question

[Should MP3 length trim the encoder's gapless padding?](08-should-mp3-length-trim-the-encoders-gapless-padding.md)
found that trimming is exact for Info/Xing files with a valid LAME tag, which is 546 of the
library's 654 MP3s. It is impossible from headers for the 103 bare files. Decide between:

- **A. Keep whole frames** for every MP3. One rule, and up to 52 ms of encoder silence stays
  inside `Length` and inside crossfades.
- **B. Trim when a validated LAME tag is present,** and keep whole frames otherwise. Two MP3
  length rules, stated per row. This needs a probe extension, a trimming reader, a permanent
  test that measures the ACM decoder's offset, and new matrix cells.

C (`iTunSMPB`) gains nothing on this library, D (a guessed trim) conflicts with ADR 0010, and E
(rejecting bare files) costs 103 files; say whether any of them deserves a second look.

If B: which validation (tag CRC, encoder allowlist, MusicLength), what a failed tag falls back
to, and whether the timeline origin moves to the trimmed start. Settle the wording change to
ADR 0010 and `docs/supported-audio.md`.

## Answer

Grilled with the user on 2026-09-23, from the research and library survey in
[Should MP3 length trim the encoder's gapless padding?](08-should-mp3-length-trim-the-encoders-gapless-padding.md).

**B, staged late: trim when a valid LAME tag is present; otherwise keep whole frames.**

### Why

The encoder silence is inaudible inside a crossfade but audible at a hard cut: up to about
50 ms of gap at every boundary. A hard cut is also the default for every unauthored adjacency.
That is exactly where continuous material lives: live albums, segued albums, mixed
compilations. The data to fix it is already in 546 of the library's 654 MP3s. The 15 bare files
in one mixed compilation stay gappy, and FLAC is already gapless.

C (`iTunSMPB`) gains nothing on this library. D (a guessed trim) is ruled out by ADR 0010. E
(rejecting bare files) costs 103 files.

### The rules

- **A valid tag passes all three checks:**
  - the LAME tag CRC matches;
  - MusicLength matches the audio bytes on disk, within 10 KiB or 5%, whichever is larger
    (catches a file cut after encoding);
  - the values are in range: delay ≤ 2880, padding ≤ 2304, and delay + padding < frames × 1152.

  There is **no encoder-string allowlist**: a passing CRC already proves LAME's layout. All 546
  tagged library files pass.
- **A trimmed file:** `Length = frames × 1152 − delay − padding`, the source's sample count.
  **0:00 is the first encoded sample,** so the playhead, from-start points and the seek bar
  measure from the real start of the music. From-end points measure from its real end.
- **A bare file, or a tag that fails validation,** keeps whole frames **silently**. It is still
  promised and plays fine; a notice would be noise the user cannot act on.
- **Nothing migrates:** no MP3 transition is persisted.

### What the build needs

- **The probe** reads the LAME extension after the optional Xing fields (Rockbox's walk),
  validates it, and issues the trimmed `Length` from the header (ADR 0010 holds).
- **A trimming reader**, in the pattern of `FlacTrackReader`: offset arithmetic over NAudio's
  already-exact seek, mapping position `p` to `p + delay + D` and ending at `Length`.
- **A permanent test that pins `D`**, the ACM decoder's output offset (529 for standard
  decoders, unmeasured for ACM), by correlating against the generator's source PCM. **If `D` is
  not stable across files and rates, the build issue stops and reports** rather than guessing.
  Switching to an open decoder (NLayer) would be a fresh decision.
- **Matrix cells:**
  - `CbrInfo` and `Vbr` assert the source count (88200 / 96000);
  - a fixture with a failing tag asserts whole frames;
  - a `LAME`-string fixture sits beside ffmpeg's `Lavc`, crafted if `lame.exe` is not
    available to the generator.
- **Docs, written when the code lands, not now:**
  - an addendum to ADR 0010, since its decision stands and only MP3's header reading gets
    richer;
  - per-row length rules in `docs/supported-audio.md`: tagged `CbrInfo`/`Vbr` use the source
    count, everything else whole frames.
- **Staging:** after the daily-driver essentials (the map's Notes).

`CONTEXT.md` is unchanged: **Length** already means the value the engine issues.
