# 01: Playlists live in the library and the open one autosaves

Status: resolved
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

- [x] Add three files with no playlist open → `Untitled.m3u8` exists with three rows before the
      user does anything else.
- [x] Move a row → the file's order changes.
- [x] Open playlist B while A's row 2 plays → row 2 keeps playing; the next boundary is
      row 2 → B's row 0 and plays the authored transition for that pair if one exists.
- [x] Rename A → C: the file is renamed and A's overrides answer to C.
- [x] Delete the open playlist while playing → playback continues; the first remaining
      playlist is open.
- [x] Quit with an empty Untitled 3 open → the file is gone on relaunch.
- [x] Under shuffle, the file keeps the unshuffled order.

## Tests

- **fake:** each acceptance line; invalid names; the join adjacency reaches the resolver with
  the right pair.

## Smoke

- None (no shell change; issues 02 and 03 cover it).

## Comments

- Built 2026-09-27 in `PlaybackEnginePlaylists.cs`, with `PlaylistFolder` (names, listing,
  refusals, "first free Untitled") in Persistence. `OpenPlaylist` returns `Task<AddReport>`: the
  rows load through the add pipeline as one job, off the lock, so a missing entry is a
  rejection in the run's one notice, as `Add` has it. Its entries are not "named", so a playlist
  inside one is not followed. `ITransitionStore` gains `RenamePlaylist` **and
  `RemovePlaylist`** (delete needs it too).
- **The join (builder note).** The sounding rows (outgoing, attended, current, in that order)
  stay at the top of the queue as **passing rows**: in `_passing`, never in `_originalOrder`,
  so never written and never exported. `OpenFirstReadable` does not step *into* a passing row
  (a repeat-one loop, a repeat-all wrap), except going back (⏮) and landing after device loss.
  The view's next-adjacency rule skips them too, but a passing row's own next is the new row 0,
  so the join is an ordinary adjacency, resolved from the store at `ScheduleBoundary`. At the
  end of every locked section, `DropSilentPassingRows` removes each passing row whose voice is
  no longer outgoing, attended or current, so no command has to know about them. Stop, Clear
  and the queue's end drop them at once. Move keeps them at the top.
- **Shuffle (builder note): no change was needed.** `_originalOrder` already is the playlist's
  own order. Autosave writes it, so a Move under shuffle changes the queue but not the file, and
  turning shuffle off returns to the file's order.
- **Autosave is written off the lock.** A command takes the rows under the lock and writes them
  after releasing it, like `SaveAsPlaylist`. A `_saveGate` orders the writes (always the latest
  rows) and every rename, delete and create, so a late write can never recreate a file that was
  renamed or deleted. An add writes at most every 500 ms and always at its end: a
  5,000-file folder would otherwise rewrite the whole file 5,000 times. So a crash in the
  middle of an add loses at most half a second of rows. A write that fails is one notice per
  playlist until a write succeeds, not one per file.
- **Loading guard.** While the open playlist's rows are still loading, the file holds rows the
  queue does not yet have. So an edit during the load is only noted, and written when the load
  ends. If the load is cut short (CancelAdd, or another playlist opened) and nothing was edited,
  nothing is written, so the file keeps every entry. One consequence to know: a playlist entry
  whose file is missing is rejected on open, and the next edit writes the playlist without it.
- **`LastPlaylist` (deviation, say if wrong).** An **empty Untitled is not remembered** until it
  gets rows. It is deleted on quit, so the next launch opens the playlist remembered before it
  rather than a fresh Untitled, and a launch on an empty library writes no settings.json.
  `ChangeSettings` keeps `LastPlaylist`, as it keeps shuffle and repeat. At launch, a
  settings file left alone (newer schema) is not overwritten to remember the Untitled.
  A settings write that fails is told once per playlist opened.
- **Names:** trimmed; refused if blank, containing a character invalid in a file name, ending in a
  full stop, a Windows device name (CON, COM1…), or over 255 characters with the extension.
  Matched ignoring case. A rename to a name another playlist has is refused; a case-only rename
  works. Success is silent (the sidebar shows it); every refusal is "Not renamed: A. …".
- **Hygiene** (an open empty Untitled deleted on OpenPlaylist, NewPlaylist, Delete's reopen,
  OpenLibrary and Dispose) checks the file too: an Untitled whose load was cut short still holds
  rows and is kept. It is housekeeping, so a failure is silent. NewPlaylist with an empty Untitled
  open does nothing: that is already the new playlist.
- Existing tests changed by the new behaviour: three expected the library's settings.json not to
  exist or to hold no playlist (it now remembers the Untitled once rows arrive), and one expected
  exactly two notices from an unwritable settings file (the first add now adds "Not saved: the
  playlist to reopen at launch").
- Tests: `PlaylistsInTheLibraryTests` (26) and `PlaylistsAcrossLaunchesTests` (4): every
  acceptance line, add in the background, Move/Remove/Clear, unwritable file, open while stopped,
  the join with an authored transition that plays and the passing row gone once silent, a
  passing row never written, a missing entry, six refused names, a case-only rename, delete of
  the last playlist and of one not open, no library, launch, gone-last-playlist and OpenLibrary.
- dotnet test: 940 passed, 0 failed (Core 762, App 178) on the last runs. Both `AddTimeBudgetTests`
  fail intermittently when Core and App run together (1,369 ms and 449 ms against 1,000 and 150).
  The parent commit does the same: 1 of 3 combined runs failed on each commit (1,910 ms on the parent). Core
  alone passed 6 of 6. One full run took 31 minutes; no hang was found, and every run since took
  18–24 s.
