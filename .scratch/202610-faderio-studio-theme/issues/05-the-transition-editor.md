# 05: The transition editor

Status: resolved
Blocked by: 02

Shell. See the [spec](../spec.md). This issue makes sure the editor panel reads well in the theme
once issue 02 has themed its controls.

## What to build

- **`TransitionEditorPanel`**:
  - The fields sit in a Well with Edge borders.
  - The radio buttons and check boxes (Global / This playlist, From end) are in Text, or Dim
    when disabled, on the panel's own ground, never a lighter box behind them.
  - The heading is in Text and the hint lines are in Dim.
  - The −/+ and −1/+1 nudges are small keys.
  - The fade curve dropdown is drawn on Well.
- **The traffic light** keeps its green, amber and red meaning. Its ring and its off state come
  from the palette.
- **The editor's scroll area** uses the native dark scroll bar.

## Acceptance

- [x] Headless: no control in the editor keeps a `SystemColors` colour, and no check box or radio
      button has a back colour different from its parent's.
- [x] Looks, both hosts, with a transition open and with none: every label is readable, the
      disabled state is visibly disabled but legible, and the traffic light is clear.
- [x] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments

2026-10-07, built.
- **Ink roles.** `StudioTheme.SetInk(control, Ink)` says what a control's text is: `Text`, `Dim`
  (a hint) or `Warning` (a refusal, in the accent). The theme sets every control's colour from its
  ink, so the theme no longer turns hints into plain text. The editor's capture hints and the
  line under the selector are Dim. A refusal and the gone-track line were Firebrick and are now
  the accent: the palette has no error role, and the accent is the look's one lit colour
  (Duophonic's is red; the built-in one is amber).
- **Check boxes and radio buttons** are painted by the theme: the box or circle on the Well with a
  Dim rim, the mark in the accent, the text in its ink, everything Dim while disabled. WinForms'
  flat ones drew a white box, and their disabled text was nearly black on the dark ground.
- **Text boxes** are framed in Edge, and so is a bordered panel (the editor's own frame). Windows
  draws that frame in its window-frame grey (`#646464`). An edit control draws it a second time
  while it paints its text, so the theme draws over it after both `WM_NCPAINT` and `WM_PAINT`.
  `DrawToBitmap` never shows the second draw, so this test uses a shown window that is off
  screen and never activated.
- **The fade curve** (and every flat dropdown): the frame is Edge, the inner rings and the button
  are Well, and the chevron is Text, or Dim while disabled. This drops WinForms' light frame and
  its grey disabled button (noted as left over in issue 02).
- **`TrafficLight`** (Shell) replaces the painted label. Green, amber and red are unchanged. Off
  (stopped) is Dim, with a 2 px ring in Edge. Its state is `Heard`, so the theme no longer
  overwrites it as it did the label's ForeColor.
- The nudges were keys already (issue 02). The scroll area already had the dark scroll bar.
- The layout is unchanged: Render is where issue 04's screenshot has it.

Looks (App), by screenshot in Duophonic's `palette.json` and the built-in palette, playing silent
WAVs with the editor open, and with no boundary open:
- No white or system-grey box is left.
- Radios and From end sit on the panel's ground, with a red or amber dot.
- Disabled, Global, This playlist and From end are dim and readable (in issue 04's shot they were
  white glyphs with near-black text).
- The hints are dim. The boxes are wells, their Edge frame pixel-sampled `#0F0F0F`. The curve is
  dark, with a dim chevron when greyed.
- The light is green while its first track plays, and the scroll bar is the dark one.

Looks (MusicBee), in the user's Duophonic skin, main panel, after installing the build:
- With no boundary open, every field is greyed, dim and legible.
- After a posted click on row 1's ✎ (nothing played), the editor shows the user's authored
  values, the Global dot in indian red, and dim hints. The traffic light is dim grey (stopped)
  and clear.
- MusicBee was not running at the start and was closed again afterwards.

Tests: net8.0 Core 1152, Shell 193, App 190; net48 x86 Core 1131, Shell 193, MusicBee 104;
0 failed.
