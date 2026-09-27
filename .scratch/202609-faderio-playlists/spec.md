# Spec — faderio v3: playlists, overrides and the manager

Status: resolved
Blocked by: [library](../202609-faderio-library/spec.md)

## Problem

A playlist is any M3U the user points at, loaded by appending and saved by dialog; there is no
name, no list, no current playlist, and edits are lost unless saved. The store carries
per-playlist overrides that nothing writes and the engine never reads. There is no way to see
how one track meets every other. The queue shows four fixed columns.

## Solution

Six slices on top of the library. Decided as D14–D25 in
[decisions.md](../202609-faderio-interim-improvements/decisions.md); **when an issue and that
file disagree, the decisions file wins.** Third of the three interim specs (D1). Glossary:
**Playlist**, **Queue**, **Transition override** in `CONTEXT.md`.

## Rules every issue inherits

- **Placement.** The three tests in `CLAUDE.md`. Playlist files, names, autosave, the join
  when a playlist opens, and override resolution are engine; the sidebar, the name label, the
  manager window and the column layout are shell.
- **Commands are total.** A playlist file that cannot be written is a notice; the queue stays as
  it is.
- **Rows, not tracks** for queue commands; playlists are named by their name string.
- **Verification.** `dotnet test` before every commit, with the counts in the message. Shell
  sessions run the Looks half of `docs/smoke.md`; each issue adds its items.

## Order

| # | Issue | Blocked by |
|---|---|---|
| 01 | [Playlists live in the library and the open one autosaves](issues/01-playlists-live-in-the-library-and-the-open-one-autosaves.md) | — |
| 02 | [The playlist sidebar](issues/02-the-playlist-sidebar.md) | 01 |
| 03 | [The open playlist's name, import and export](issues/03-the-open-playlists-name-import-and-export.md) | 01 |
| 04 | [Overrides: resolved by the engine, authored in the panel](issues/04-overrides-resolved-by-the-engine-authored-in-the-panel.md) | 01 |
| 05 | [The transition manager](issues/05-the-transition-manager.md) | 04 |
| 06 | [Configurable columns](issues/06-configurable-columns.md) | — |

## Out of scope

Drag-to-add onto a sidebar playlist (D16), stable playlist ids (D15), album gain or ReplayGain
column affecting playback (D25), year/genre columns, editing from the manager (D24), smart or
auto playlists, and folders of playlists.

## Comments

- Resolved 2026-09-27: issues 01–06 built, each recorded in its own file. Three loose ends found
  in issues 05 and 06 went to [phase 3](../202609-faderio-phase-3/spec.md): ✎ out of view with
  extra columns, a gone track opened from the manager, and the flaky add-time budget test.
