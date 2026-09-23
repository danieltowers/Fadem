# 03: Open and save playlists

Status: resolved
Blocked by: 01

Decided in [What does queue management add?](../../202609-faderio-phase-1-restage/issues/12-what-does-queue-management-add.md) (M3U).
Carries v2 0003's intent (story 31).

## What to build

- **Load:** `Add` accepts `.m3u` and `.m3u8`, and appends.
  - Relative paths resolve against the playlist's folder.
  - A missing file is a rejection.
  - A `.m3u` that is not valid UTF-8 is read in the system code page.
- **Save:** **`SaveAsPlaylist(path)`** writes the visible order as UTF-8 `.m3u8`, with
  absolute paths and `#EXTINF` in whole seconds.
- The static `M3UPlaylist` moves behind the engine.
- The shell gains **Open playlist…** (which calls `Add`) and **Save as playlist…**.
- There is no "replace the queue".

## Where the logic lives

- **Engine:** parsing, path resolution, encoding and writing. The shell never reads
  `View.Queue` to write a file (placement test 1).
- **Shell:** two file dialogs.

## Acceptance

- [x] Save then load round-trips a queue, including paths with `†` and `⭐️`.
- [x] A playlist with relative paths loads correctly from any working directory.
- [x] Under shuffle, save writes the shuffled (visible) order.
- [x] A missing entry is reported as a rejection; the others load.

## Tests

- **fake:** round-trip with non-ASCII paths, relative resolution, the code-page fallback, and
  missing entries. The existing `M3U_*` unit tests move or go.
- **form:** both dialogs reach the engine.

## Smoke

- **Looks:** Save as playlist… then Open playlist… doubles the queue in the same order.

## Comments

- Built 2026-09-23. `M3UPlaylist` is internal to `Faderio.Core.Playback`. The add worker reads a
  playlist named to `Add` off the lock, the way it lists a folder, and puts its entries where it
  stood. So `Add(file, playlist, file)` keeps its order, and the playlist counts as one path in
  `View.Adding` until it is read. Entries are files: a playlist naming a playlist, or itself, is
  not followed; that entry is probed and rejected as not a promised format. A folder entry is
  still listed. An entry that is not a path at all (a NUL, say) stays as written and is rejected
  alone. `.m3u8` is read as UTF-8. A `.m3u` is read as strict UTF-8 first, then in the system ANSI
  code page. A byte-order mark wins over both. `file:///` URIs are not decoded; they are
  rejected as missing.
- `SaveAsPlaylist(path)` takes the tracks under the lock and writes off it: UTF-8 without a BOM,
  `#EXTINF:<rounded seconds>,<Artist - Title>`, absolute paths. It writes a uniquely named temp
  file beside the target, then renames it over the target. It is total: the outcome is a notice
  ("Saved N tracks to name." or "Could not save the playlist: reason"). The engine writes
  whatever path it is given. The dialog offers only `.m3u8`.
- Known gap: `Add` keeps the paths it is given, and `Write` makes them absolute at save time. A
  track added from a relative launch argument is therefore saved against the working directory
  at save time. Dialogs and drops always give absolute paths.
- Shell: **Add files…**, **Add folder…**, **Open playlist…** (multi-select) and **Save as
  playlist…** now share a row above the transport. Four buttons in the transport row would have
  left the seek bar a stub at the minimum width. The dialogs sit behind `ChoosePlaylists` and
  `ChoosePlaylistSavePath`.
- Tests: `PlaylistTests` (9) and three form tests. The `M3U_*` tests in `PlaybackQueueTests`
  are gone. The relative-path and shuffle tests were each seen to fail under a mutation. The
  code-page test asserts that its bytes are not UTF-8, so on a machine whose ANSI code page is
  UTF-8 it fails rather than passing vacuously.
- Smoke found [issue 19](19-stop-can-freeze-the-window.md), a freeze on Stop that predates
  this change.
