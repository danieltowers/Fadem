# 01: Playlists live in the library and the open one autosaves

Status: ready-for-agent
Blocked by: —

Decided in [D14, D15, D16 (join), D17, D20](../../202609-faderio-interim-improvements/decisions.md),
ADR 0011. Amends ADR 0003's "there is no replace the queue".

## What to build

- **The store:** `<library>/.faderio/playlists/<name>.m3u8`, written by the existing
  `M3UPlaylist` writer (UTF-8, `#EXTINF`, absolute paths, atomic). The name is the file name
  without extension; characters invalid in a file name are refused with a notice. Listing the
  folder is listing the playlists, sorted by name, case-insensitive.
- **`View.Playlists`** (`IReadOnlyList<string>` of names) and **`View.OpenPlaylist`** (name).
- **`OpenPlaylist(name)`**: replaces the queue with the file's rows. If a voice is sounding it
  keeps sounding; the queue's remaining rows behind it are replaced by the new rows so that its
  row → new row 0 is the next adjacency, resolved from the store like any other (D16). If
  nothing sounds the queue is simply replaced and row 0 attended at 0:00. Unreadable / missing
  entries are handled as `Add` handles them today.
  - *Builder note:* the sounding row is not in the new playlist. Keep it as a row the queue
    passes out of and does not autosave; once its voice falls silent it is gone. Record the
    mechanism here.
- **`NewPlaylist()`**: creates "Untitled" (then "Untitled 2", …, first free number), opens it.
- **`RenamePlaylist(oldName, newName)`**: renames the file and rekeys the store's overrides
  from old to new (`ITransitionStore` gains `RenamePlaylist`). Refuses with a notice if the
  target exists or the name is invalid.
- **`DeletePlaylist(name)`**: deletes the file and removes its overrides. If it was open: the
  first remaining playlist opens by the D16 join rule, or a fresh Untitled if none (D17).
- **Autosave:** every command that changes the queue's rows — `Add`, `Remove`, `Move`,
  `Clear`, and the completion of each file during an add — writes the open playlist. The
  written order is the playlist's own order, **not** the shuffle order; the builder checks how
  shuffle is represented in the engine and records here whether that needed a change.
- **Untitled hygiene:** on `OpenPlaylist`, `OpenLibrary` and `Dispose`, an open Untitled-N
  with no rows is deleted.
- **Launch:** the library's last open playlist (a `LastPlaylist` key in the library
  `settings.json`) opens, row 0 attended at 0:00; if none, or it is missing, Untitled (D20).
- `SaveAsPlaylist(path)` stays as the export primitive; `Add` of an `.m3u` path stays as the
  import primitive (issue 03 puts the buttons on them).

## Where the logic lives

- **Engine:** all of it. The shell never writes a playlist file and never lists the folder.

## Acceptance

- [ ] Add three files with no playlist open → `Untitled.m3u8` exists with three rows before the
      user does anything else.
- [ ] Move a row → the file's order changes.
- [ ] Open playlist B while A's row 2 plays → row 2 keeps playing; the next boundary is
      row 2 → B's row 0 and plays the authored transition for that pair if one exists.
- [ ] Rename A → C: the file is renamed and A's overrides answer to C.
- [ ] Delete the open playlist while playing → playback continues; the first remaining
      playlist is open.
- [ ] Quit with an empty Untitled 3 open → the file is gone on relaunch.
- [ ] Under shuffle, the file keeps the unshuffled order.

## Tests

- **fake:** each acceptance line; invalid names; the join adjacency reaches the resolver with
  the right pair.

## Smoke

- None (no shell change; issues 02 and 03 cover it).

## Comments
