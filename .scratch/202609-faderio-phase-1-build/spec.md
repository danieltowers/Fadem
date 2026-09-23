# Spec — faderio v3: the rest of Phase 1

Status: ready-for-agent

## Problem

The slice proved a transition you can trust, on two tracks. It is not yet a daily driver: the
queue cannot take a folder or a playlist, rows cannot be removed or moved, broken files and
lost devices leave the engine in doubt, settings are launch arguments, and the editor, preview
and ReplayGain are incomplete. v2's Phase 1 had all of this against a seam that no longer
exists.

## Solution

Eighteen vertical slices, each an engine change (where needed), its shell, its tests at the
right ADR 0002 seam, and its `docs/smoke.md` items. Every decision was settled on the map
[faderio v3: restage the rest of Phase 1](../202609-faderio-phase-1-restage/map.md). The issues
cite the ticket that decided them and restate only what a builder must check. **When an issue
and its ticket disagree, the ticket wins.**

v2's issues are cited as *v2 0003* and so on. They live, read-only, in
`v2-GLM-5.3-flash\.scratch\202609-faderio-build\faderio-phase-1\`.
Take intent from them, never code.

## Rules every issue inherits

- **Placement.** The three tests in `CLAUDE.md`. Each issue says where its logic lives. New
  engine members follow `codebase-design`; measuring work follows `tdd`.
- **Audible time.** The `View` describes what the listener hears; commands act at the decode
  position as soon as possible and are never held back to line up with the playhead (ADR 0001
  addendum).
- **Commands are total.** No engine command throws for a user-reachable condition. Failures
  become notices ([issue 09](issues/09-notices-a-strip-and-a-session-list.md) builds the list).
  Nothing is swallowed.
- **Rows, not tracks.** Commands that act on the queue take `RowId`s; a preview and a clip name
  tracks. See **Row** in `CONTEXT.md`.
- **Verification.** `dotnet test` before every commit, with the counts in the message. Any
  shell change runs the Looks half of `docs/smoke.md` and reports item by item. The Sounds half
  is the user's.

## Order

Daily driver first; `Blocked by:` binds within that.

| # | Issue | Blocked by |
|---|---|---|
| 01 | [Adding runs in the background](issues/01-adding-runs-in-the-background.md) | — |
| 02 | [Add a folder, or drop files on the queue](issues/02-add-a-folder-or-drop-files-on-the-queue.md) | 01 |
| 03 | [Open and save playlists](issues/03-open-and-save-playlists.md) | 01 |
| 04 | [Remove rows and clear the queue](issues/04-remove-rows-and-clear-the-queue.md) | — |
| 05 | [Reorder rows by dragging](issues/05-reorder-rows-by-dragging.md) | 04 |
| 06 | [Skip, previous and the end of the queue](issues/06-skip-previous-and-the-end-of-the-queue.md) | — |
| 07 | [Shuffle and repeat controls](issues/07-shuffle-and-repeat-controls.md) | 06 |
| 08 | [Transition now](issues/08-transition-now.md) | 06 |
| 09 | [Notices: a strip and a session list](issues/09-notices-a-strip-and-a-session-list.md) | — |
| 10 | [Unreadable tracks are passed over](issues/10-unreadable-tracks-are-passed-over.md) | 06, 09 |
| 11 | [Device loss and the output fallback chain](issues/11-device-loss-and-the-output-fallback-chain.md) | 09 |
| 12 | [Settings: a file, a dialog and the default crossfade](issues/12-settings-a-file-a-dialog-and-the-default-crossfade.md) | 09 |
| 13 | [ReplayGain switches live](issues/13-replaygain-switches-live.md) | 12 |
| 14 | [Editor: curve, from-end, revert, remove, validation](issues/14-editor-curve-from-end-revert-remove-validation.md) | — |
| 15 | [Editor: every adjacency, and unsaved edits](issues/15-editor-every-adjacency-and-unsaved-edits.md) | 06, 14 |
| 16 | [Preview](issues/16-preview.md) | 10, 15 |
| 17 | [The clip light](issues/17-the-clip-light.md) | 16 |
| 18 | [Trim tagged MP3s](issues/18-trim-tagged-mp3s.md) | — (staged last) |

01–13 make the daily driver; 14–17 complete authoring; 18 is gapless polish.

## Out of scope

As the map's Out of scope section lists, including: an authored "A → end" fade, loudness
scanning, queue persistence across restarts, album gain, stable track identity, formats
rejected today, per-playlist override UI, and **automated tests of `NAudioAudioOutput`**. The
last is ruled out here: ADR 0002 leaves the device to the human seam, and
[issue 11](issues/11-device-loss-and-the-output-fallback-chain.md) adds the Sounds items that
prove its paths.
