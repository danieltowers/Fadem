# 02: The main window's chrome and keys

Status: ready-for-agent
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

- [ ] Headless: no control in either host's main view keeps a `SystemColors` back or fore
      colour. Every push button is owner-drawn. A key paints its LED only when on.
- [ ] Looks (App): the main window in `Palette.Studio` has no white or system-grey surface, and
      the title bar is dark. Keys read as keys, and ⏸ and the toggles light their LEDs. Report
      this item by item with screenshots.
- [ ] Looks (MusicBee): in Duophonic, the panel's chrome meets MusicBee's `#232323` with no
      visible seam.
- [ ] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments
