# 18: Trim tagged MP3s

Status: resolved
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

- [x] `CbrInfo` and `Vbr` fixtures issue and decode the source count (88200 / 96000).
- [x] Their first samples correlate with the generator's source PCM at offset 0.
- [x] A failing-tag fixture and `CbrBare` keep whole frames.
- [x] A `LAME`-string fixture sits beside ffmpeg's `Lavc`, crafted if `lame.exe` is
      unavailable.
- [x] Seek-landing cells still pass.

## Tests

- **real:** the matrix cells above, plus the `D` measurement as a permanent test.

## Smoke

- **Sounds:** two consecutive tracks from a continuous MP3 album, with no transition authored,
  hard-cut with no audible gap.

## Comments

- Built 2026-09-24.
  - **D is stable: 529.** Every tagged fixture (both rates, mono and stereo, CBR and VBR) and
    two bare ones put the source's first sample at exactly 576 + 529 = 1105 in the raw ACM
    decode, found by correlating against the FLAC noise fixture of the same generator source.
    The stop condition did not trigger. `Mp3DecoderOffsetTests` pins it on all 12 MP3 cells,
    and asserts the header's whole frames are what NAudio decodes.
  - **Probe:** MP3 header reading moved out of `AudioProbe` into `Mp3Header`, shared with the
    reader. It walks the Xing fields to the LAME extension and applies the ticket's three
    checks exactly; a failure keeps whole frames silently.
  - **Found: two tag CRC conventions.** The spec says "the first 190 bytes". LAME covers the
    frame up to the CRC; ffmpeg covers 190 bytes with the CRC field zero and a short frame
    zero-padded. Same bytes for stereo, different for mono: every mono fixture failed until
    the probe accepted either. The library survey was stereo-heavy, which is why it never
    showed.
  - **Found: 61 library files record padding 0** (60 `Lavc57`, one `LAME3.x`; 59 in one
    compilation album), less than D, so the source's last samples lie past the last frame.
    `Mp3TrackReader` plays those as silence (up to 529 samples, 12 ms) so the decode still
    ends at the issued `Length`. A file cut short of its last frame is not padded; it ends
    with its notice as before.
  - **Reader:** `Mp3TrackReader`, offset arithmetic over `Mp3FileReader`'s exact seek. Files
    without a valid tag get the plain `Mp3FileReader`.
  - **Fixtures:** `lame.exe` is not available, so the `LAME`-string and failing-tag files are
    crafted at test time by `CraftedMp3` from the checked-in fixtures (no ffmpeg needed). A
    guard test proves its CRC reproduces ffmpeg's bytes.
  - **Docs:** ADR 0010 addendum; per-row length rules in `docs/supported-audio.md`, which also
    now says the ACM codec is the one closed decoder; a Sounds item in `docs/smoke.md`;
    `generate.sh` notes the crafted cells and that the FLAC noise is the MP3s' source PCM.
  - **No shell change,** so the Looks half was not run.
  - **Not done:** the Sounds item (hard cut on a continuous MP3 album, no gap) is the user's.
