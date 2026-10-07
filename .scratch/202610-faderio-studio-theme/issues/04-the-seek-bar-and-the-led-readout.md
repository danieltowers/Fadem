# 04: The seek bar and the LED readout

Status: resolved
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

- [x] Headless: a seek bar painted into a bitmap with a palette uses Accent for the played part
      and the thumb, Fade inside a wedge, and no `SystemColors.Highlight`.
- [ ] Looks, both hosts, while playing a track with authored fades (App: done; MusicBee: stopped
      only, see Comments): the played part, the thumb,
      the wedges and the marks are all clear on the dark ground. The readout is large, in the
      accent, and not clipped. It still fits the MusicBee sidebar (284 px) without wrapping
      the transport.
- [x] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments

2026-10-07, built.
- `SeekBar.Palette` (default `Palette.Studio`), set by `StudioTheme` like every other control.
  Ground Chrome, groove Edge, played part and thumb Accent, wedges Fade, marks and handles Mark,
  ticks and labels Dim (the minor ticks fainter). The fixed constants are gone. Its old pixel
  tests are restated in the roles: "marked" is off the Chrome ground, a wedge is Fade.
- `Readout` (Shell), used by both hosts: `Readout.Time()` is Consolas 15 pt, which `StudioTheme`
  sets in Accent; `Readout.Seconds()` keeps the small face, in Dim. The time draws at 15 pt where
  it fits and steps down by half points where it does not (never below 8), so it is never cut:
  hinted glyphs do not scale exactly, so the proportional size alone overshot by 3 px in a test.
- Row heights: each host's times table is 28 px for the LED line and 18 for the seconds, within
  the transport's unchanged 50 px.
- The plugin's readout column is 180 px in a main panel (as the App's, full size) and 120 in a
  sidebar (was 110), where "0:00 / -0:04" draws at about 14.5 pt and the seek bar keeps over
  60 px of travel. Tested headlessly at 285 px; the seek row stays one line.

Looks (App), playing two generated silent WAVs (no sound) with authored fades, in the built-in
palette and in Duophonic's: the played part, thumb, wedge and marks are clear on the dark ground;
the readout is large, in the accent, and whole; ⏸ lights. Under Duophonic the marks are the text
colour (the accent is red), and the still-to-play groove, Edge `#0F0F0F` on `#1C1C1C`, is faint
but visible.

Looks (MusicBee), stopped only: its queue holds the user's real tracks, so nothing was played
there. In Duophonic the readout first drew at about 13 pt in the main panel (the fixed 120 px
column), which is why the column now widens there; re-checked, it is full size and whole. The
played part, wedges and marks in MusicBee are covered by the App run and the shared tests, not
seen in MusicBee itself. S: was offline, so the queue rejected the playlist's two S: tracks;
the playlist file was left as it was.

Tests: net8.0 Core 1152, Shell 187, App 190; net48 x86 Core 1131, Shell 187, MusicBee 104;
0 failed.
