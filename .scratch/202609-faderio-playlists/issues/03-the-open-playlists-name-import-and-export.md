# 03: The open playlist's name, import and export

Status: ready-for-agent
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

- [ ] The label shows the open playlist; F2, type, Enter renames it in the sidebar too.
- [ ] Import a relative-path `.m3u` from another folder: the library copy has absolute paths
      and is open.
- [ ] Export writes the visible order to the chosen path and leaves the library copy alone.
- [ ] Add folder while "Untitled 2" is open → its file grows.

## Tests

- **fake:** `ImportPlaylist` copies, absolutises, numbers a clash, opens; a missing file is a
  notice.
- **form:** label edit calls `RenamePlaylist`; the buttons call their commands.

## Smoke

- **Looks:** the label above the queue and the title bar both name the open playlist.

## Comments
