# 04: The seek bar and the LED readout

Status: ready-for-agent
Blocked by: 02

Shell. See the [spec](../spec.md), "The look": the seek bar and the LED readout.

## What to build

- **`SeekBar`** takes a palette, and draws:
  - the ground in Chrome;
  - the groove in Edge;
  - the played part and the thumb in Accent;
  - the fade wedges in Fade;
  - the marks and their handles in Mark;
  - the ticks and labels in Dim.

  Its fixed colour constants go.
- **The readout** in both hosts ("0:14 / -3:03"): Consolas at about 15 pt in Accent. The seconds
  line under it stays small, in Dim. The transport row's height is fitted so neither is clipped.

## Acceptance

- [ ] Headless: a seek bar painted into a bitmap with a palette uses Accent for the played part
      and the thumb, Fade inside a wedge, and no `SystemColors.Highlight`.
- [ ] Looks, both hosts, while playing a track with authored fades: the played part, the thumb,
      the wedges and the marks are all clear on the dark ground. The readout is large, in the
      accent, and not clipped. It still fits the MusicBee sidebar (284 px) without wrapping
      the transport.
- [ ] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments
