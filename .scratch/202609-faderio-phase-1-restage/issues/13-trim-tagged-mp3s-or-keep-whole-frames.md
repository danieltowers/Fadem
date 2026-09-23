# Trim tagged MP3s, or keep whole frames?

Type: grilling
Status: open
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
