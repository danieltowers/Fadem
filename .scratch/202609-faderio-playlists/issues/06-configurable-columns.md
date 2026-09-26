# 06: Configurable columns

Status: ready-for-agent
Blocked by: —

Decided in [D25](../../202609-faderio-interim-improvements/decisions.md).

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

- [ ] Tick Replay gain and Album; both columns appear with values for a tagged FLAC.
- [ ] Drag Album before Artist; resize; switch libraries and back → layout restored.
- [ ] A library with no `columns.json` shows the default four.

## Tests

- **unit (Core):** the reader fills Album; the probe's format lands on the track.
- **form:** the header menu toggles a column; `columns.json` round-trips order and widths.

## Smoke

- **Looks:** right-click the header, tick Path; a Path column appears with a full path.

## Comments
