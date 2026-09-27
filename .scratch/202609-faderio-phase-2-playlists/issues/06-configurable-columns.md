# 06: Configurable columns

Status: resolved
Blocked by: —

Decided in [D25](../../202609-faderio-phase-2-interim-improvements/decisions.md).

## What happens today

Four fixed columns: #, Title, Artist, Length (`MainForm.cs:172-175`). `Track` carries
`FilePath, Title, Artist, Length, ReplayGainDb`. TagLib reads title, artist and ReplayGain only;
the probe learns sample rate, bit depth and channels but does not keep them on the track.

## What to build

- **Track metadata:** `Album` (TagLib, empty if absent) and `Format` (sample rate, bit depth,
  channels, as the probe already determines them) join `Track`. `ReplayGainDb` stays. The
  add-time cost is nil: the tag is already open and the header already read.
- **Columns available:** #, Title, Artist, Album, Length, Format ("44.1 kHz / 16-bit"),
  Replay gain ("−7.2 dB" or blank), Path. The row-button column from the transport spec is
  fixed at the far right and not in this set. `#` is always shown.
- **Gestures:** right-click on the header → a checklist of the columns; drag a header to
  reorder; resize to set width. WinForms `ListView` supports reorder (`AllowColumnReorder`)
  and width events; the builder confirms and records.
- **Persistence:** `<library>/.faderio/columns.json` — order, visibility, widths — **owned by
  the shell** (it is view state, not engine state; the library issue 03 set the precedent with
  `window.json`). Read on library open, written on every change. Missing or corrupt → the
  default four columns.
- The Presenter formats Format and Replay gain; the engine supplies the values (placement
  test 2).

## Where the logic lives

- **Engine:** the two new fields.
- **Shell:** everything else.

## Acceptance

- [x] Tick Replay gain and Album; both columns appear with values for a tagged FLAC.
- [x] Drag Album before Artist; resize; switch libraries and back → layout restored.
- [x] A library with no `columns.json` shows the default four.

## Tests

- **unit (Core):** the reader fills Album; the probe's format lands on the track.
- **form:** the header menu toggles a column; `columns.json` round-trips order and widths.

## Smoke

- **Looks:** right-click the header, tick Path; a Path column appears with a full path.

## Comments

- Built 2026-09-27. **Engine:** `Track` gains `Album` (TagLib's `Tag.Album`, empty when absent)
  and `Format`, the probe's `AudioShape` (format, sample rate, bit depth, channels), null only for
  a track never probed (the manager's gone file). `TrackMetadataReader.Read` takes the shape
  beside the length, and a file whose tags cannot be read still gets both. Nothing new is read.
- **Shell:** `ColumnLayout.cs` holds `QueueColumn` and the layout: every column in its order,
  shown or not, with its width. `Toggle`, `Move`, `Resize`; `Load` is the default four for a file
  that is missing, empty, corrupt, of another shape or naming a column this version lacks; a file
  naming only some columns keeps them and adds the rest hidden. Widths outside 24–4000 are the
  default. Names on disk, not numbers. `FileIn(library)` is `<library>\.faderio\columns.json`, or
  none for a library that did not open (default columns, not written). `Presenter` has
  `ColumnHeading`, `QueueCell`, `FmtFormat` ("44.1 kHz / 16-bit"; an MP3's rate alone) and
  `FmtReplayGain` ("−7.2 dB" with a true minus, "+1.5 dB", blank when untagged, "0.0 dB" for
  what rounds to nothing).
- **Decided here: # is pinned first, ✎ last.** The list draws its first column from each row's
  own text, which is the row label, so # cannot move. A drop on # lands after it and a drop on ✎
  before it. `ColumnReordered` is cancelled and the layout moves the column; the columns and rows
  are made again in its order, so the column index is always the display index and ✎ is always
  `Columns.Count - 1` (`MainForm.EditColumn` became an instance property). Rows keep their
  selection through it.
- **Header right-click.** A `ListView` shows its `ContextMenuStrip` for a right-click on the header
  too. The queue menu's `Opening` checks whether the cursor is on the header (`LVM_GETHEADER`,
  `GetWindowRect`), cancels, and shows the column menu. It is shown by `BeginInvoke`: shown from
  inside the cancelled `Opening`, it never appeared. # is ticked and greyed.
- **Widths.** A dragged width is kept and written once it rests (400 ms after the last change),
  and before a library switch or the window closing. Toggle and move write at once. Title still
  fills what the others leave, and Artist gives way first, now back up to its own width as room
  returns (it used to shrink only). **Found in smoke:** making the list's handle replays every
  width as a change (`ListView.OnHandleCreated` → `UpdateColumnWidths`), including the fitted
  title's, so the first `columns.json` held Title at 317. The shell now remembers each width it
  set and treats only a different one as the user's; a form test pins it (fails at 317 without).
  A drag below 24 px stops at 24; the menu hides.
- **Not handled: ✎ out of view.** With more columns than the list's width (Album, Format and
  Replay gain in a 1240 px window), the list scrolls sideways and ✎ is off the right until the
  user scrolls or narrows a column. The fit only takes from Title and Artist.
- Tests: `TrackMetadataTests` (Core, 7): album with title, artist and ReplayGain from a tagged FLAC
  (tagged by the new `TestAudio.WriteTaggedFlac`); empty album untagged; the format of a FLAC, a
  24-bit mono FLAC, an MP3 and a WAV on the track; the format kept when TagLib refuses the file.
  `ColumnLayoutTests` (App, 15): default, toggle, four drops, # and hidden stay, round trip, six bad
  files, a partial file. Presenter (13): Format, Replay gain, every cell, headings. Form (8, new
  partial `HeadlessFormTests.Columns.cs`): the default four and the menu's ticks; ticking Replay
  gain and Album shows the tagged FLAC's values and writes the file, unticking hides; Path and
  Format; selection, ✎ and its bold kept through a toggle; Album moved before Artist and resized to
  150 comes back after `OpenLibrary` elsewhere and back, the other library untouched; drops on #
  and ✎; a corrupt file is the default; fitted widths are not the user's. Mutations checked: no
  write on a library switch, and a resize never recorded, each fail the round-trip test.
- dotnet test: 1068 passed, 0 failed (Core 809, App 259). `AddTimeBudgetTests`'s 8 MB cover-art
  test failed 2 of 5 full runs (166 ms against 150) and passes alone, as recorded in issue 05.
- Looks smoke, fresh temp library, second library and machine folders; a copy of the 44.1 kHz
  16-bit tone FLAC tagged "Quiet Song" / Example Band / First Album / −7.20 dB and an untagged 48 kHz 24-bit
  one. **Default:** #, Title, Artist, Length, ✎; no `columns.json`. **Header right-click** (posted
  `WM_CONTEXTMENU`, cursor on the header): the checklist #, Title, Artist, Album, Length, Format,
  Replay gain, Path, with # ticked and greyed and the default four ticked. **Path** ticked: a Path
  column with each row's full path (cut short by its 360 px), ✎ still rightmost and in view.
  **Album, Format, Replay gain** ticked: "First Album", "44.1 kHz / 16-bit", "−7.2 dB" on the tagged
  rows, blank Album and Replay gain and "48 kHz / 24-bit" on the untagged; the list scrolled
  sideways (above). **Drag** (posted header drag, window topmost for the moment): Album before
  Artist moved it and wrote the file; Album's divider from 200 to 150 held and was written once
  it rested; Format dropped on ✎ landed before it; Length dropped on # landed after it. **Open
  library…** on a second folder: the default four; back: the layout as left, Album 150; the
  second folder has no `columns.json`. **Relaunch** on the first library: the same layout. A
  right-click on a row still shows the row menu.
