# Should MP3 length trim the encoder's gapless padding?

Type: research
Status: open
Blocked by:

## Question

Findings: branch research/mp3-gapless-padding, docs/research/mp3-gapless-padding.md

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
