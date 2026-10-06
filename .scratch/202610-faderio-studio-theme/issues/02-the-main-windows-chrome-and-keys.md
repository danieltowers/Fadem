# 02: The main window's chrome and keys

Status: resolved
Blocked by: 01

Shell and both hosts' main views. See the [spec](../spec.md), "The look": chrome and wells,
hardware-key buttons, text boxes and lists.

## What to build

- **`Faderio.Shell/Theme/StudioTheme.Apply(Control, Palette)`**: walks a control tree once.
  - Surfaces: Chrome, and Well for lists, text boxes and the editor's fields.
  - Text: Text, or Dim when disabled.
  - Keys: every push button and button-look check box, owner-drawn. A rounded key (4 px radius)
    with a top-lit gradient from Key, an Edge outline, a top shine, and Raised on hover. An LED
    in Accent when on: ⏸ while playing, or a checked toggle. A disabled key keeps its shape
    with its glyph in Dim.
  - Text boxes: flat with an Edge border. Dropdowns: owner-drawn on Well.
  - The native dark theme on lists and scroll areas, and on the title bar, when `IsDark`.
- Both hosts apply it to their main view at construction, with their palette from issue 01.
  Controls a view makes later are themed as they are added (`ControlAdded`).

## Acceptance

- [x] Headless: no control in either host's main view keeps a `SystemColors` back or fore
      colour. Every push button is owner-drawn. A key paints its LED only when on.
- [x] Looks (App): the main window in `Palette.Studio` has no white or system-grey surface, and
      the title bar is dark. Keys read as keys, and ⏸ and the toggles light their LEDs. Report
      this item by item with screenshots.
- [x] Looks (MusicBee): in Duophonic, the panel's chrome meets MusicBee's `#1C1C1C` bars with no
      visible seam.
- [x] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments

2026-10-06, built. `StudioTheme.Apply` keeps each control's
palette in a `ConditionalWeakTable`, and themes later children through `ControlAdded`. Keys are
painted over a flat button in its `Paint` event. `StudioTheme.Light` lights ⏸ while playing, and
a checked button-look toggle lights itself. To keep the queue from being white rows on a dark
list, the rows, the selection, the header's background and the open playlist moved to the
palette here (see issue 03). Both hosts now take the palette as a constructor argument.

Looks (App), by screenshot with the built-in palette and with Duophonic's `palette.json`:
- Chrome, wells, keys, flat text boxes and the dark title bar are right.
- Disabled keys keep their shape with a dim glyph.
- The LEDs were checked headlessly only: a pixel test on a key drawn to a bitmap, and ⏸ lit
  while playing. No screenshot shows them, because playing would sound on the user's speakers.
- Left for later issues: the fade curve dropdown's light frame (05), the notice link's blue
  (06), the readout (04), and the column rules down the queue's empty part (03).

Looks (MusicBee), checked once S: was back.
- The first look had a seam: the palette's chrome was element 0's `#232323`, but MusicBee draws
  its own bars in element 2's `#1C1C1C`.
- The plugin now takes Chrome from element 2 and Wells from element 16's border (`#121212`), with
  the Edge derived (`#0F0F0F`). The spec's table says so.
- Re-checked: the panel meets MusicBee's bars with no seam (pixel-sampled), and MusicBee wrote the
  new `palette.json`. The user has MusicBee's "always prompt for library selection" ticked, so
  each restart stops at its chooser until "library" is selected.

Tests: net8.0 Core 1152, Shell 181, App 187; net48 x86 Core 1131, Shell 181, MusicBee 103;
0 failed. One App run hung once and passed on a re-run in 71 s. Later runs used
`--blame-hang`, and none hung. In the final full run, one MusicBee test failed once and passed in
two re-runs (103 of 103). It was not identified: that run kept no per-test record.
