# Spec — faderio v3: the studio console look, in MusicBee's skin colours

Status: resolved
Blocked by: none

Raised 2026-10-05: "the whole app, its just default windows bog standard at the moment. how do
we bring the ui to life?" Settled 2026-10-06 by the UI prototype: **B, studio console, wins.
Make it read my MusicBee skin.**

## Problem

faderio draws in stock Windows controls: grey system buttons, a white list with the system's
header, white text boxes and scroll bars. Inside the user's dark MusicBee skin (Dark\Duophonic),
the plugin is a bright white box in the middle of a dark window. The standalone app looks the
same.

## The prototype and its verdict

Branch `prototype/ui-themes` (commit `efc6c81`), throwaway, never for main. It put four looks
over the real main window, with a floating switcher (`pwsh
src/Faderio.App/Prototype/run-prototype-theme.ps1 -Theme B`): today's, A MusicBee Duophonic
(flat, monochrome), **B Studio console**, and C Neon DJ. **B won.** Its structure is the design.
Its fixed graphite and amber colours are not: those come from the host's skin, all but the
accent, which is faderio's own blue (round 2 issue 07, 2026-10-09).

## Solution

### The look (from B)

- **Chrome and wells.** The window and toolbars are the chrome. The queue, the playlists and the
  editor's fields sit in darker **recessed wells**, with a hairline edge.
- **Hardware-key buttons.** Every push button, the transport's included, is a rounded key (4 px
  radius). It has a top-lit gradient, a dark edge and a one-pixel shine along its top, and it
  lightens on hover. A key that is "on" (⏸ while playing, a checked toggle such as 🔀, 🔁 or
  Sync editor) shows a small **LED** in the accent at its top-right. A disabled key keeps its
  shape, with its glyph dimmed.
- **The LED readout.** The time readout ("0:14 / -3:03") is set large in a monospaced face
  (Consolas, about 15 pt), in the accent.
- **Section labels.** Pane titles (PLAYLISTS, QUEUE, TRANSITION EDITOR) are small bold capitals
  in the accent. The queue's header is drawn the same way: small capitals, dim, on the well,
  with a hairline under it.
- **The queue.** The playing row's text is in the accent. The selected row is a raised tint.
  An unreadable row is dim. ✎ is the plain text colour when authored and dim when not.
- **The seek bar.** The groove is the edge colour, and the played part and the thumb are the
  accent. Fade wedges are the accent at low strength, and the fade marks are the mark colour.
  The scale and its labels are dim.
- **Text boxes and lists.** Text boxes are flat on the well with a hairline border. Dropdowns are
  drawn on the well. Scroll bars and the list header's empty end use Windows' own dark theme
  (`DarkMode_Explorer`, `DarkMode_ItemsView`) when the palette is dark, and the light one when
  it is not. The title bar goes dark with it (`DWMWA_USE_IMMERSIVE_DARK_MODE`).
- **Menus** (the queue's, the header's, the section menu) use a renderer in the palette's colours.
- **Section groups.** Their headers are drawn in the palette, not Windows blue. The prototype
  left them stock; this needs owner-drawn groups or the colours `ListGroups` can reach.

### The colours: a palette of roles, from the host

The look is drawn only from a **palette**: ten roles, never a hard-coded colour.

| Role | Used for | In MusicBee, read from (`Setting_GetSkinElementColour`) | Duophonic gives |
|---|---|---|---|
| Chrome | window, toolbars, seek bar ground | the colour MusicBee draws the strip above the panel in: element 2, default, background, or element 0, state 1, background, whichever is lighter on a light skin and darker on a dark one (2026-10-08) | `#1C1C1C` |
| Well | queue, playlists, editor fields | element 16, default, border | `#121212` |
| Edge | wells' hairlines, key edges, groove | Chrome, 45% toward black | `#0F0F0F` |
| Key | button face | element 7 (input control), default, background | `#2C2C2C` |
| Text | text | element 0, default, foreground | `#E1E1E1` |
| Dim | headers, scale, disabled, unreadable | element 7, state 3, foreground | `#6D6D6D` |
| Raised | selection, hover | Key, 8% toward white (toward black on a light skin) | `#3D3D3D` |
| Accent | LED, readout, playing row, played, labels | fixed `#0078D7`, Windows' default selection blue, on every skin (round 2 issue 07); the skin's own (element 7, state 4, foreground) is not read | `#0078D7` |
| Fade | the seek bar's wedges | Accent at 30% over Chrome | derived |
| Mark | fade marks and handles | red `#E0453A`, or Text where the Accent is itself red, so the marks stand apart from the played part | `#E0453A` |

These values were measured on 2026-10-06 by a temporary probe in the installed plugin. The probe
called every element from −12 to 40, every state from 0 to 12 and every component from 0 to 6.
(The first mapping took Chrome from element 0, `#232323`. In MusicBee that left a seam: MusicBee
draws its own bars in element 2's `#1C1C1C`. Issue 02 moved the mapping one step darker.) Only
elements −1, 0, 2, 7, 10, 14, 15, 16 and 17 answer; every other combination is black. The
plugin's `MusicBeeInterface.cs` names only five elements and two states, so the spec names the
numbers it found. State 4's foreground (`#CD5C5C`, indian red) is the skin's one colour that is
not grey. It was the accent until round 2 issue 07 (2026-10-09) made the accent a fixed blue.

- **Rules over the raw values.** A role whose read is black where its neighbours are not (an
  element a skin leaves out) falls back to the role it derives from: Well → Chrome, Edge →
  Chrome darkened, Key → Chrome lightened. The accent is `#0078D7` on dark and light skins
  alike; on a Well where it has less than 3:1 it moves a fifth of the way toward Text until it
  has it. (Until round 2 issue 07 the accent was the skin's, with amber `#FFB000` and deep amber
  `#8A5000` as fallbacks; both are gone.) Text that has too little contrast on
  Well (below 4.5:1) is pushed to white or black. A light skin gives a light palette, and the
  same look works on it: keys, wells and LEDs are all drawn from the roles.
- **When it is read.** The plugin reads the palette as its view is made, and again whenever
  MusicBee makes the panel afresh. Whether MusicBee re-makes the panel on a skin change, or
  needs a restart, is checked at the smoke step and recorded.
- **The standalone app** has no MusicBee to ask. It uses the palette the plugin last read on
  this machine, which the plugin writes to the app's machine folder (`%APPDATA%\faderio\palette.json`)
  each time it reads one. With none, it uses a built-in Studio palette: B's graphite, with the blue accent.
  The two hosts on one machine then look alike, and an app user without MusicBee still gets the
  look.

## Where it lives

- **`Faderio.Shell/Theme/`**:
  - `Palette`: the roles, the derivation and fallback rules, and the built-in palette. It is
    pure and unit-tested.
  - `StudioTheme`: applies a palette to a control tree, a dialog included. It owner-draws keys,
    labels, combos and the header, and sets the native dark theme on lists and scroll areas.
  - The menu renderer.
- **Each host** supplies the palette: the plugin from MusicBee's API, the app from
  `palette.json` or the built-in one. This is shell view state, never the engine's: no test in
  CLAUDE.md's "where logic belongs" is touched.
- **The custom-drawn parts** (`SeekBar`, `QueueRowStyle`, `PlaylistSidebar`, `PlaylistNameBar`,
  `TransitionEditorPanel`'s traffic light) take their colours from the palette instead of
  `SystemColors` and fixed constants. `SectionPalette`'s eight section colours stay as they are:
  they are the user's own choices.
- **Every window and dialog** gets the theme, not only the main window: Settings, the transition
  manager, the library chooser, the notice list, faderio's MusicBee Configure dialog, and the
  section name dialog.

## Testing

- **`Palette`** (net8.0 and net48): Duophonic's measured values give the table's roles. A
  accent is `#0078D7` whatever the skin answers, pulled toward Text on a Well it fails 3:1 on.
  A black role falls back to the role it derives from.
  Low-contrast text is pushed. A light skin gives a light palette whose contrast holds.
- **Headless controls:** a themed tree has no `SystemColors` left in any colour, the original
  greys included. A key paints its LED only when on. The readout uses the monospaced face. The
  queue's playing row is in the accent.
- **Plugin:** a fake MusicBee answering the measured Duophonic values gives the view that
  palette, and writes `palette.json`. A fake that answers black everywhere gives the built-in one.
- **App:** a `palette.json` present is used, and a missing or broken one gives the built-in one.

## Smoke

Replace the "stock look" assumptions in `docs/smoke.md`'s Looks halves.
- **App:** the window is the studio look in the built-in palette, or in the plugin's when
  `palette.json` exists. No white or system-grey surface is left on the main window or any
  dialog.
- **MusicBee:** in the user's Duophonic skin, faderio's panel blends with MusicBee's chrome
  (`#1C1C1C`) with indian-red LEDs and readout. Switch to a light skin: the panel follows, after
  a restart if MusicBee needs one, and that is recorded.

## Decisions (accepted by the user, 2026-10-06)

1. **The accent is the skin's own** (Duophonic: indian red `#CD5C5C`), not B's amber. Amber only
   when the skin has no accent. *Superseded 2026-10-09 by round 2 issue 07: the accent is a
   fixed `#0078D7` on every skin.*
2. **The app follows the plugin's last palette** through `palette.json`, else the built-in
   Studio palette. The alternative is that the app always uses the built-in palette.
3. **No theme setting** in this slice: no light/dark toggle, no picker. The skin decides. A
   picker can follow.
4. **Built in issues by surface**, each shippable alone: palette and plugin read; main window
   chrome and keys; queue and header; seek bar and readout; editor; dialogs and menus; section
   groups.

## Out of scope

- The waveform on the seek bar (its own spec, `202610-faderio-seek-bar-waveform`). It will take
  its tint from this palette.
- Custom icons in place of the transport's glyph characters.
- Animation (LED fades, button press travel).
- Re-laying out any window: the look changes, the layout stays.

## Comments

2026-10-08, the light-skin check (Smoke, MusicBee), in the **portable** MusicBee at the user's
wish (their installed one left alone), with this build installed there.
- **After a restart:** the portable started on Neutral\CalciumMETRO (set in its ini, closed).
  The panel was in that skin's palette: chrome `#BEBEBE`, wells `#D7D7D7`, keys white, text
  black, `IsDark` false. The section header, swatch and stripes read; `palette.json` was
  rewritten with it.
- **Live, no restart needed:** View → Skins → MusicBee3 while it ran. MusicBee rebuilt its
  main window (a new handle), the plugin's view was made afresh and read the skin again, and the
  panel came up in MusicBee3's palette (chrome `#FFFFFF`, wells `#DFDFDF`), blending with
  MusicBee's white bars. So the spec's open question is answered: **MusicBee re-makes the panel
  on a skin change; no restart.**
- Restored after: the portable's ini, and `%APPDATA%aderio\palette.json` (it holds the last
  skin read, so it held MusicBee3's light palette until restored).

Found on both light skins, and fixed the same day (below):
1. **The accent is near black.** Element 7, state 4's foreground is `#330000` on CalciumMETRO
   and `#291212` on MusicBee3: saturated, so the monochrome fallback keeps it, but beside black
   Text it does not read as lit. The section labels, the LED readout and the playing row look
   black; the playing row barely stands out. A rule for an accent too close to Text in
   lightness (or contrast) would send it to a fallback that reads on a light Well; amber
   `#FFB000` itself is weak on `#D7D7D7`, so a light palette may want a darker fallback.
2. **CalciumMETRO's chrome leaves a seam:** the panel is `#BEBEBE` (element 2, background) but
   MusicBee draws its tab and header strips in `#EBEBEB`. Duophonic and MusicBee3 match.

2026-10-08, both fixed, at the user's word ("use a deep amber").
- **The near-black accent was ours, not the skins'.** A temporary probe build (never committed)
  dumped every answer in the portable MusicBee: the raw accents are vivid, `#FF0000` on
  CalciumMETRO and `#CD5C5C` on MusicBee3. The contrast rule meant to move an accent a fifth of
  the way toward Text per step, but `Mix`'s arguments were the wrong way round, so one step kept
  only a fifth of the accent: `#FF0000` → `#330000`. Fixed, the light skins keep their reds,
  `#CC0000` and `#A44A4A`; Duophonic's `#CD5C5C` already reads and is unchanged.
- **Deep amber `#8A5000`** (4.5:1 on CalciumMETRO's well) is the light skins' fallback, for a
  grey accent and, new, for one too close to the text (below 1.5:1); amber stays the dark
  skins'. Neither measured light skin needs it.
- **The chrome:** MusicBee draws the strip above the panel in `#EBEBEB` on CalciumMETRO, which is
  element 0, state 1 (the tab bar's), not element 2 (`#BEBEBE`). On MusicBee3 it is element 2's
  white and element 0, state 1 is `#E6E6E6`; on Duophonic element 2's `#1C1C1C` against
  `#202020`. So the host reads both (`SkinColours.Bars`, element 0, state 1, component 1) and
  the palette takes the lighter on a light skin, the darker on a dark one. That rule fits the
  three skins measured, no more.
- Tests (`PaletteTests`): the light skins' measured answers give their reds and their chrome;
  Duophonic's chrome holds with its element 0, state 1 added to its fixture; a grey or
  text-like accent on a light skin is deep amber. All four failed on the old rules.
- Looks, the portable MusicBee started on each light skin with this build: CalciumMETRO's labels,
  readout and playing row red, and no seam against MusicBee's strip; MusicBee3's in its red,
  chrome still white. The portable's ini and `palette.json` restored after; the installed
  MusicBee untouched (its Duophonic skin file was copied into the portable for the probe, then
  removed).
