# 18: Trim tagged MP3s

Status: ready-for-agent
Blocked by: — (staged last: after the daily-driver essentials)

Decided in [Trim tagged MP3s, or keep whole frames?](../../202609-faderio-phase-1-restage/issues/13-trim-tagged-mp3s-or-keep-whole-frames.md),
from the research in
[Should MP3 length trim the encoder's gapless padding?](../../202609-faderio-phase-1-restage/issues/08-should-mp3-length-trim-the-encoders-gapless-padding.md)
(branch `research/mp3-gapless-padding`, never merged).

## What to build

The ticket holds the rules. In summary:

- **The probe** validates the LAME tag (CRC, MusicLength and range) and issues
  `Length = frames × 1152 − delay − padding`, with 0:00 at the first encoded sample.
- **Bare files and failing tags** keep whole frames, silently.
- **A trimming reader** does offset arithmetic over NAudio's exact seek.
- **A permanent test pins the ACM decoder's offset `D`.**
- **Docs:** ADR 0010 gets an addendum, and `docs/supported-audio.md` gets per-row length
  rules, written when the code lands.

**Stop condition:** if `D` is not stable across files and rates, stop and report. Do not
guess, and do not switch decoders.

## Where the logic lives

- **Engine (Core):** the probe, the reader and the test. There is no shell change.

## Acceptance

- [ ] `CbrInfo` and `Vbr` fixtures issue and decode the source count (88200 / 96000).
- [ ] Their first samples correlate with the generator's source PCM at offset 0.
- [ ] A failing-tag fixture and `CbrBare` keep whole frames.
- [ ] A `LAME`-string fixture sits beside ffmpeg's `Lavc`, crafted if `lame.exe` is
      unavailable.
- [ ] Seek-landing cells still pass.

## Tests

- **real:** the matrix cells above, plus the `D` measurement as a permanent test.

## Smoke

- **Sounds:** two consecutive tracks from a continuous MP3 album, with no transition authored,
  hard-cut with no audible gap.
