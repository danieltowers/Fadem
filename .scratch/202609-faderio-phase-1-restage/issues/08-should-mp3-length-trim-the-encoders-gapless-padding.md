# Should MP3 length trim the encoder's gapless padding?

Type: research
Status: resolved
Blocked by:

## Question

Findings: branch `research/mp3-gapless-padding`, `docs/research/mp3-gapless-padding.md`

Ticket 14 of the predecessor measured that an MP3's `Length` is whole 1152-frame blocks, so
it carries encoder delay at the start and padding at the end, up to about 50 ms each. An
authored from-end point measures against that `Length`, and the silence sits inside any
crossfade.

Find out, from primary sources:

- Where LAME records encoder delay and padding (the LAME tag inside the Xing/Info frame), and
  how reliably other encoders write it; what the VBRI header offers.
- How NAudio's `Mp3FileReader` treats those frames today, and what trimming would need.
- What other gapless players do (foobar2000, the iTunes `iTunSMPB` convention).

The answer is whether trimming is feasible for all three promised MP3 variants in
`docs/supported-audio.md`, and what it would change about `Length` and the matrix. The
decision itself goes into the build issues via ticket 11.

## Answer

Researched on 2026-09-23. The findings live on branch `research/mp3-gapless-padding`
(`76626fa`, plus the library survey addendum in `c7788f9`), in
`docs/research/mp3-gapless-padding.md`. That branch is never merged.

**Feasibility, per promised variant:**

- **`CbrInfo` and `Vbr`: trimming is exact from the header.** LAME's extension to the Info/Xing
  frame holds encoder delay and padding. Then `Length = frames × 1152 − delay − padding`, which
  is the encoded source's sample count. ffmpeg decodes the fixtures to exactly that. Both
  variants share one code path.
- **`CbrBare`: trimming is not possible from headers.** A bare file carries no delay or padding
  anywhere. `iTunSMPB` would cover iTunes files only, and the library has none.
- **One number is unmeasured.** Where Windows' closed ACM decoder puts the 529-sample decoder
  delay. A trimming reader needs it, and it would be pinned by a permanent correlation test.
  Getting it wrong leaves up to 12 ms of the wrong audio at each end, but moves no `Length`.
- **The ticket overstated the silence.** It is always 1105 samples at the start (25 ms at
  44.1 kHz) and 47–1198 samples at the end (up to 27 ms).

**The library, surveyed from headers** (all 654 MP3s, `docs/research/mp3-lame-tag-survey.py`):

- **551 are Info-tagged and 103 are bare.** None is Xing-tagged.
- **546 (83% of the MP3s) carry a LAME or Lavc tag that passes both the CRC and the
  MusicLength checks.** Every delay is 576.
- **No bare file has `iTunSMPB` or VBRI.**
- **No MP3 transition is persisted**, so trimming would migrate nothing.

**What trimming would change:**

- `Length` for tagged MP3s, and the wording of ADR 0010 and `docs/supported-audio.md`.
- The matrix cells: `CbrInfo` and `Vbr` would assert the source count; `CbrBare` keeps whole
  frames, which gives MP3 two length rules.
- New cells: a `LAME`-encoded file and a failing-tag fallback.
- A from-end point on a trimmed file moves earlier by the tail trim. A from-start point moves
  by 1105 samples if the origin moves.

**Options** (research §5): A. do not trim; B. trim when a validated LAME tag is present,
otherwise whole frames; C. B plus `iTunSMPB` (gains nothing here); D. guess a trim for bare
files (conflicts with ADR 0010); E. reject bare files (103 library files).

The decision is not research's to make. It graduates into
[Trim tagged MP3s, or keep whole frames?](13-trim-tagged-mp3s-or-keep-whole-frames.md),
which now blocks [Stage the build issues](11-stage-the-build-issues.md).
