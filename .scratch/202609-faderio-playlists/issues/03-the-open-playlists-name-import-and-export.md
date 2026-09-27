# 03: The open playlist's name, import and export

Status: resolved
Blocked by: 01

Decided in [D18, D19](../../202609-faderio-interim-improvements/decisions.md).

## What to build

- **Name label** above the queue list, reading `View.OpenPlaylist` in a larger font.
  Double-click or F2 turns it into an inline text box; Enter → `RenamePlaylist`, Esc reverts.
- **Title bar:** `<playlist> — faderio — <library>`; the library issue 02 format gains the
  playlist in front.
- **Import playlist…** replaces "Open playlist…": a file dialog for `*.m3u;*.m3u8`, multi-
  select. Each chosen file is copied into the library's playlists folder as `<file name>.m3u8`
  (numbered if taken) with its paths made absolute, then the last one is opened. This is a new
  engine command, **`ImportPlaylist(path)`**, built on the existing M3U reader and writer; the
  shell never copies files.
- **Export playlist…** replaces "Save as playlist…": a save dialog defaulting to
  `<open playlist>.m3u8`, calling the existing `SaveAsPlaylist(path)`.
- **Add files… / Add folder… / drop** keep calling `Add`; they append to the open playlist,
  which autosaves (issue 01). Nothing to build; verify it.

## Where the logic lives

- **Engine:** `ImportPlaylist`.
- **Shell:** the label, its inline edit, two dialogs, the title.

## Acceptance

- [x] The label shows the open playlist; F2, type, Enter renames it in the sidebar too.
- [x] Import a relative-path `.m3u` from another folder: the library copy has absolute paths
      and is open.
- [x] Export writes the visible order to the chosen path and leaves the library copy alone.
- [x] Add folder while "Untitled 2" is open → its file grows.

## Tests

- **fake:** `ImportPlaylist` copies, absolutises, numbers a clash, opens; a missing file is a
  notice.
- **form:** label edit calls `RenamePlaylist`; the buttons call their commands.

## Smoke

- **Looks:** the label above the queue and the title bar both name the open playlist.

## Comments

- Built 2026-09-27. **Engine:** `ImportPlaylists(IEnumerable<string> paths)` in
  `PlaybackEnginePlaylists.cs`, returning the opened playlist's load report as `OpenPlaylist`
  does. Each source is read with `M3UPlaylist.Read` (which already resolves a relative entry
  against the source's folder) and written with a new `M3UPlaylist.WriteEntries`, which shares
  the atomic write with `Write`. `PlaylistFolder.FirstFree(name, existing)` numbers a clash as
  Untitled is numbered ("Evening 2"); `FirstFreeUntitled` now calls it.
- **One call for the selection (deviation, say if wrong).** The issue names `ImportPlaylist(path)`,
  but the dialog is multi-select and only the last file opens. One call per file would have the
  shell sequence N engine calls for one intent (placement test 1), each opening its playlist in
  turn. So it is `ImportPlaylists(paths)`: every file is copied, then only the last one copied
  opens, through the same `SwitchTo` as `OpenPlaylist` (the join, empty-Untitled hygiene,
  `LastPlaylist`).
- **What the copy holds.** `#EXTM3U` and the entries, absolute, with no `#EXTINF`. The entries
  are not probed until the playlist opens, and the first edit rewrites the file with `#EXTINF`
  as autosave does. An entry that is missing is still copied and is rejected when the playlist
  loads, as `OpenPlaylist` rejects one. The source is never written.
- **Notices.** Success is one notice for the selection: "Imported Evening." or "Imported
  evening.m3u8 as evening 2." when numbered, because the source's name is not otherwise shown.
  Each file refused is its own notice, headed "Not imported: <file name>.": the file does not
  exist, it is not `.m3u`/`.m3u8`, it could not be read, its name cannot name a playlist, or its
  copy could not be written. The others are still imported, and the last one that was imported
  opens. With no library open: "Not imported. No library is open."
- **Shell.** `PlaylistNameBar.cs` is a panel docked top between the sidebar and the editor, over
  the queue. It shows `View.OpenPlaylist` in 13 pt, or a grey "No playlist open" with no library.
  A double-click on the name, or **F2 in the queue**, swaps in a text box holding the name, all
  selected. The label is not focusable, so F2 is taken where the playlist's rows are. Enter calls
  `RenamePlaylist(old, typed)`, Esc calls nothing, and leaving the box commits, as the sidebar's
  in-place edit does. A render does not overwrite the box while it is up. The title is
  `Presenter.Title(playlist, …)`: "Evening — faderio — lib · WASAPI shared at 48000 Hz", and with
  no playlist open it is as before.
- **Buttons.** **Import playlist…** opens a multi-select dialog filtered to `*.m3u;*.m3u8` (the
  old "All files" entry is gone). Once something is picked, it settles the editor's unsaved edits
  and empties it (`LeaveEditor`, as a sidebar click does), then makes one call. **Export
  playlist…** opens a save dialog on "<open playlist>.m3u8" and calls `SaveAsPlaylist`, which
  writes the visible order. Neither the shell nor the engine touches the library copy on export.
  `ChoosePlaylistSavePath` became `ChooseExportPath(fileName)`.
- **Add files / folder / drop:** nothing built. `AddFolder_WhileUntitled2IsOpen_GrowsItsFile` checks
  that the file grows.
- Docs: `docs/smoke.md` title item and the old Save-as/Open item rewritten, and a name-label item
  added. ADR 0003 gains an addendum naming the new dialogs and `ImportPlaylists`.
- Seen in the smoke, not changed: a refused rename does not publish, so a playlist file copied into
  the folder from outside ("Morning") was listed by the engine but reached the sidebar only at the
  next import. This is issue 02's re-listing note: it shows at the next *successful* playlist
  command.
- Tests: `ImportPlaylistTests` (9, fake: a relative-path `.m3u` copied absolute and opened, the
  source untouched; a clash numbered and told; several files, the last one opening; a missing file
  among others; only missing files; not a playlist; a copy that cannot be written; a missing entry
  copied and rejected on load; no library). Form tests: the import and export buttons (5, replacing
  the three Open/Save tests), the name bar (6: launch and New, F2 + Enter renames in the sidebar
  and title, double-click starts, Esc, a refused rename, a render while editing) and Add folder
  into Untitled 2. Presenter: the title with a playlist in front. Two title expectations in older
  form tests gained "Untitled — ".
- dotnet test: 974 passed, 0 failed (Core 771, App 203).
- Looks smoke, fresh temp library and machine folders, two generated tones with `--edit-first`:
  "Untitled" in the larger font above the queue, beside the sidebar, and the title "Untitled —
  faderio — lib03 · WASAPI shared at 48000 Hz". The button row reads Add files…, Add folder…,
  Import playlist…, Export playlist…, Open library…, Settings…. F2 in the queue shows the box
  holding "Untitled" all selected, and Enter with "Evening" renames the label, the sidebar, the
  title and the file. A double-click opens the box too, and Esc leaves "Evening". "morning" (with
  Morning.m3u8 put in the folder) is refused with "Not renamed: Evening. There is already a
  playlist called Morning." and the label stays. Export offered "Evening.m3u8". Saved into a
  folder named `playlists † ⭐️`, the strip read "Saved 2 tracks to Evening.m3u8.", the file
  held the two rows in order, and the library copy was byte-for-byte as before. Import's dialog
  filter reads "Playlists (M3U, M3U8) (*.m3u;*.m3u8)". Importing that file opened "Evening 2"
  with the same two rows, highlighted in the sidebar, in the label and the title, with "Imported
  Evening.m3u8 as Evening 2." and the editor emptied. A `.m3u` of `..udio\…` entries from
  another folder imported as "Relative mix" with absolute paths, the source unchanged. At the
  960×480 minimum, ✎ is in view with no sideways scroll bar. One capture showed Stop preview
  dark in the emptied editor, but UIA reports it disabled, so this is the capture.
