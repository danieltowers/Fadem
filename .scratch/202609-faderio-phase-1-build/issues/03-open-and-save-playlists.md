# 03: Open and save playlists

Status: ready-for-agent
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

- [ ] Save then load round-trips a queue, including paths with `†` and `⭐️`.
- [ ] A playlist with relative paths loads correctly from any working directory.
- [ ] Under shuffle, save writes the shuffled (visible) order.
- [ ] A missing entry is reported as a rejection; the others load.

## Tests

- **fake:** round-trip with non-ASCII paths, relative resolution, the code-page fallback, and
  missing entries. The existing `M3U_*` unit tests move or go.
- **form:** both dialogs reach the engine.

## Smoke

- **Looks:** Save as playlist… then Open playlist… doubles the queue in the same order.
