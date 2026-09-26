# 02: The playlist sidebar

Status: ready-for-agent
Blocked by: 01

Decided in [D16, D17](../../202609-faderio-interim-improvements/decisions.md).

## What to build

- A left-docked panel, ~200 px, between the transport row and the status strip, listing
  `View.Playlists`. The open one (`View.OpenPlaylist`) is highlighted.
- **Click** a playlist → `OpenPlaylist(name)`. If the editor has unsaved edits, the existing
  Save/Discard/Cancel prompt runs first.
- **Right-click** menu: **New playlist** (`NewPlaylist()`), **Rename** (inline edit of the
  label; Enter commits via `RenamePlaylist`, Esc cancels; F2 and double-click also start it),
  **Delete** (`DeletePlaylist`, after a plain Yes/No confirm — the file is gone for good).
- A refused rename (name taken, invalid) shows the engine's notice and the label reverts.
- The panel re-renders from the view on every publish; it holds no list of its own (placement
  test 3).

## Where the logic lives

- **Shell only.** Every gesture is one engine call.

## Acceptance

- [ ] The sidebar lists every `.m3u8` in the library's playlists folder, open one highlighted.
- [ ] Click opens; rename inline; delete asks once.
- [ ] A file dropped into the folder outside the app appears after the next publish (the
      engine re-lists on open; if it does not, note here that it lists only on library open).

## Tests

- **form:** click calls `OpenPlaylist`; rename commit calls `RenamePlaylist` with old and new;
  Esc calls nothing; delete calls `DeletePlaylist` after confirm.

## Smoke

- **Looks:** the sidebar shows "Untitled" highlighted at launch in a fresh temp library; New
  playlist adds "Untitled 2".

## Comments
