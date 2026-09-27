# 02: The playlist sidebar

Status: resolved
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

- [x] The sidebar lists every `.m3u8` in the library's playlists folder, open one highlighted.
- [x] Click opens; rename inline; delete asks once.
- [x] A file dropped into the folder outside the app appears after the next publish (the
      engine re-lists on open; if it does not, note here that it lists only on library open).
      *Partly: it appears at the next playlist command, not the next publish. See Comments.*

## Tests

- **form:** click calls `OpenPlaylist`; rename commit calls `RenamePlaylist` with old and new;
  Esc calls nothing; delete calls `DeletePlaylist` after confirm.

## Smoke

- **Looks:** the sidebar shows "Untitled" highlighted at launch in a fresh temp library; New
  playlist adds "Untitled 2".

## Comments

- Built 2026-09-27 in `PlaylistSidebar.cs` (a `Panel`: a "Playlists" heading over a one-column
  `ListView`), docked left in `MainForm` so it sits between the transport row and the status
  strip, beside the editor. `Render(view)` brings the items up to `View.Playlists` in place,
  touching only what differs, as it runs on every tick; the open one is bold on
  `SystemColors.Info`, the attended row's yellow. Selection is left to the user and is not the
  highlight, so a right-click on another playlist does not fight the render.
- **Gestures.** Left click and Enter → `OpenPlaylist`; the open one is no call. Menu: New playlist,
  Rename (F2), Delete. Rename also starts on F2 and a double-click (the click before it opens that
  playlist first). The ListView's own slow-click edit is refused in `BeforeLabelEdit`: only a rename
  that was asked for starts. `AfterLabelEdit` always cancels the edit and, if Enter gave a label,
  calls `RenamePlaylist(old, typed)`. A success comes back as the view's new name, and a refusal
  leaves the old one with the engine's notice. The list is not re-rendered while a label is being
  edited.
- **The editor (decision, say if wrong).** The Save/Discard/Cancel prompt runs before every
  gesture that changes the open playlist: click, New, and Delete of the open one. Delete asks
  after its Yes/No. Once settled, the editor **empties** (`TransitionEditorPanel.Close`), as Open
  library does, because what it had open was the queue left. Otherwise Discard would leave the
  discarded draft on screen. Issue 04's per-playlist overrides will want this too.
- **Window size (deviation).** The sidebar takes 200 px from the queue, so `MinimumSize` goes
  from 760 to **960** wide and the default from 1040×600 to **1240×600**. At 760 the queue got
  about 200 px and lost the ✎ column to a sideways scroll. A window.json saved narrower than 960
  opens at 960. `docs/smoke.md`'s ✎ item now reads 960, and two window tests changed: a saved
  width of 900 became 1000, and the default 1040 became 1240.
- **Re-listing (acceptance line 3).** The engine lists the folder on every playlist command
  (open, new, rename, delete) and on library open and launch, not on every publish. A file
  copied in from outside therefore shows at the next of those. Seen in the smoke: three seconds
  of render ticks did not show it, and the next Delete did. Listing on every publish would read
  the folder once per file during a large add, so it was left as is.
- Tests: 15 new form tests in `HeadlessFormTests` (launch shows Untitled highlighted; New adds
  Untitled 2; click opens and the highlight follows; unsaved edits ask, Cancel stays, Discard
  empties the editor; a click on the open one asks nothing; Enter renames with old and new; Esc
  calls nothing; a refused rename keeps the label with the notice; an unasked edit is refused;
  the menu offers Rename/Delete only on a selection; Delete asks once and No keeps it; Yes
  deletes the file; deleting the open one opens the first remaining; deleting the open one
  with unsaved edits asks after the confirm).
- dotnet test: 955 passed, 0 failed (Core 762, App 193). One earlier combined run failed
  `AddTimeBudgetTests.AddingEveryFixture_StaysWithinOneSecond`, the timing flake noted in issue
  01. The next run passed.
- Looks smoke (the sidebar items, and the layout items the sidebar could move), fresh temp
  library and machine folders, two generated tones as arguments with `--edit-first`: the
  sidebar shows "Untitled" bold on yellow at launch; the title, the button row and the transport
  row are unchanged. A right-click on empty space shows New playlist enabled and Rename/Delete
  greyed out. New playlist adds "Untitled 2", highlighted, and empties the queue and the editor.
  Clicking "Untitled" opens it with its two rows, and the empty Untitled 2 is gone. F2 opens the
  in-place editor on "Untitled", and Enter with "Evening" renames the file. A double-click on a
  new Untitled followed by "evening" is refused ("Not renamed: Untitled. There is already a
  playlist called Evening."), and the label stays. Esc after typing leaves "Evening". Delete shows
  "Delete the playlist "Evening"? Its file is deleted for good." (Yes/No, No the default); No
  keeps it. Yes on the open Untitled opens the first remaining playlist. At the 960 px minimum,
  ✎ is in view with no sideways scroll bar.
