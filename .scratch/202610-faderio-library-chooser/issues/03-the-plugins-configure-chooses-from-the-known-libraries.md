# 03: The plugin's Configure chooses from the known libraries

Status: resolved
Blocked by: 02

Plugin. See the [spec](../spec.md), D6 and D7.

## What to build

- In `SettingsDialog`, the *Library folder* text box becomes read-only, captioned *Library:*,
  and **Browse…** becomes **Choose…**.
  **Choose…** shows the shared `LibraryChooser`, with the pending choice selected.
- A choice fills the line only. `Host.SaveSettings` still opens it on MusicBee's Save (D7).
  `Accepts()`' full-path check goes away, because a chosen folder is always a full path.
- Remove in the chooser calls `ForgetLibrary` straight away (D6).
- The "No faderio library is chosen" notice in `Host.cs` still points to Configure.
- `docs/smoke.md` and the plugin smoke memories: Configure's library row.

## Acceptance

- [x] Configure shows the library open now. **Choose…** lists the plugin's known libraries.
      A choice shows in the line and opens on Save, and Cancel in Preferences discards it.
- [x] A library chosen and saved appears at the top next time.
- [x] Shell tests pass on net8.0 and net48 x86. The Looks half of the smoke is run in MusicBee
      and reported item by item.

## Comments

2026-10-04, built. The box stays a read-only text box holding the folder (blank when none is
chosen) rather than a name-and-folder line: the folder's leaf is its name, and the box keeps
its accessible name for the MusicBee smoke scripts. `Host.ShowLibraryChooser` is the seam
tests replace; it lives on `Host`, not `Plugin` or the dialog, so no type MusicBee loads before
the plugin's resolver names a Shell or Core type.

Smoke in the portable MusicBee: Configure… shows *Library:* with Choose…; Choose… lists the
plugin's own known libraries, the open one selected; a gone one removed is written to
`AppData\faderio\settings.json` before any Save; Open fills the box and nothing switches until
Preferences' Save, which opens it, and Choose… again lists it first. In MusicBee's process the
chooser's list is a UIA pane: rows are selected by posted Home/Down keys to its SysListView32.
