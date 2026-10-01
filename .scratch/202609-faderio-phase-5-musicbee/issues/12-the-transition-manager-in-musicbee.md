# 12: The transition manager in MusicBee

Status: resolved
Blocked by: 05, 09, 10

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 10, ADR
0012). Staged at the verdict; specified below (2026-10-01) before it was built.

## What to build

The shared transition manager opened from the plugin's view: the library's transitions listed,
changed and removed as in standalone faderio, **gone** tracks included. MusicBee's
auto-organise moves files, and transitions are keyed by path (ADR 0005), so tracks it moves
show as gone here.

## Implementation decisions

- **The shared manager, the view's.** `PluginView` owns one `TransitionManagerForm`, made on
  first use and reused, as `MainForm` does: a modeless tool window owned by MusicBee's main
  window (the view's `FindForm()`), shown through `ShowTransitionManager` as the notice list is
  shown, hidden by its close box and Esc, and disposed with the view when MusicBee disposes the
  panel. The view's `Render` renders it on every tick, so the picker and the grid follow the
  store as the App's do.
- **Opened from the queue's menu.** The queue's menu gains **Transitions…**, after the three
  Edit transition items and before Remove, always enabled: the manager lists the whole store,
  not the queue. It opens on the one selected row's track, else on the store's first track, as
  the App's Transitions… button does (its menu item "Transitions for this track…" needs a row
  because the button covers the rest; the plugin has one item for both). The queue's own
  **Transitions…** is reachable with an empty queue too: the list's menu shows on any right-click
  in it.
- **Changing goes through the editor.** A double-click on a manager row opens the view's
  editor on that adjacency, as in standalone faderio: in a main panel it fills the editor at
  the right; in a sidebar the page shows in the lists' place. MusicBee's window comes forward
  after, as the App's window does, so the editor is seen. The manager stays open beside it.
- **Preview from the manager sounds through the plugin's output.** `TransitionManagerForm`
  gains the editor's `beforeSounding` hook, raised before `PreviewStored` runs; the view's calls
  MusicBee's `Player_Stop` when faderio makes no sound now, as ▶ and the editor's preview do.
  Over a playing queue or a running preview nothing is stopped: faderio has the device. The
  App passes no hook.
- **Gone tracks are already the shared code's.** The manager reads a gone track as "⚠ path",
  greyed, in the picker and the grid, and the editor opened on it can remove but not change
  (phase 3 issue 02). Nothing is added for them here; the plugin's tests prove it in MusicBee,
  with a file moved from under the store as auto-organise would move it.
- **Placement.** Every string is the Presenter's or the manager's; the view reads the engine's
  view at a click and forgets it. Which track to open on is read from the published queue, not
  held.

## Testing decisions

- **Seam 2, through the plugin's entry point**, `PluginTests.Manager` (net48 x86, STA), on the
  fake output and the TestTemp library, driving what `OnDockablePanelCreated` put in the panel:
  - the queue's **Transitions…** with row 1 selected shows the manager on row 1's track,
    listing the authored crossfade with its values; with no row selected, on the store's first
    track; one manager, reused.
  - **Preview** on a row from a stopped queue calls `Player_Stop` before the output pulls a
    frame, then the output plays, the queue held where it was; Stop preview stops the output.
    **Preview global** over a playing queue calls `Player_Stop` no second time.
  - a double-click (`EditAt`) opens the view's editor on that adjacency; in a sidebar the page
    shows with the lists hidden.
  - **a gone track:** a transition authored to a third file that is then moved away lists as
    "⚠ path", greyed, in the grid and the picker; `EditAt` on it opens the editor read-only
    with Remove live; Remove takes the row out of the manager and the pair out of the store.
  - a transition saved in the view's editor while the manager is open appears in its grid.
  - disposing the panel disposes the manager.
- **The shared library's tests** (both targets): the manager raises `beforeSounding` before
  the engine previews, and not for Stop preview.
- **The App's tests** pass unchanged.
- **Seam 3, the MusicBee Looks half** of `docs/smoke.md`, gains the manager: the menu item,
  the window owned by MusicBee's, its rows, Preview with the queue held, a double-click into
  the editor, and a gone track's row with its removal. The Sounds half is the user's.

## Acceptance criteria

- [x] The manager lists, changes and removes the library's transitions from inside MusicBee.
- [x] A gone track's transitions can be removed, not changed.
- [x] The MusicBee Looks smoke covers the manager.

## Comments

- **Resolved 2026-10-01.** Built as specified above.
  - **The shell:** `TransitionManagerForm` gains the editor's `beforeSounding` hook, raised
    before `PreviewStored`; the App passes none.
  - **The plugin:** `PluginView` owns one manager, reused, shown through `ShowTransitionManager`
    (owned by MusicBee's main window) and disposed with the view; rendered on every tick; opened
    from the queue menu's new **Transitions…** (after the Edit transition items, always live) on
    the one selected row's track, else the store's first; a double-click opens the view's editor
    and brings MusicBee's window forward; the manager's previews stop MusicBee's player first
    when faderio is silent, as the editor's do.
  - **Tests:** `PluginTests.Manager` (six: the menu item on the selected row's track and on the
    store's first with one manager reused; Preview with `Player_Stop` first and the queue held,
    then Stop preview; Preview global over a playing queue stopping nothing again; a
    double-click into the editor as the sidebar page; a gone track greyed with its path, its
    editor removing but not changing, and the row and the store pair gone after; a save while
    the manager is open appearing in it, and the panel's disposal taking the manager); the
    shell's hook test on both targets. Checked by mutation: the plugin's hook.
  - **The MusicBee Looks half** on the portable MusicBee, WASAPI shared at 48 kHz, in the
    right sidebar as the user left it (376 px wide, about 330 px tall), with a → tone-c-880
    added to the smoke library's `transitions.json` for a path that is not there:
    - ✅ The queue's menu reads Edit transition: this → next, previous → this, **Transitions…**,
      Remove, Clear queue; Transitions… live with no row selected.
    - ✅ With no row selected: "faderio — transitions" opens, owned by MusicBee's main window
      (`GW_OWNER`), on the store's first track, which is the gone one: the picker reads
      "⚠ C:\…	one-c-880.wav" greyed, the grid "← tone-a-440" with 5 | –, 1 | –, Equal-power.
    - ✅ With row 1 selected (posted keys: a Windows toast covered the sidebar): the picker
      reads tone-a-440, the legend "Each value reads global | override in Untitled…", the grid
      "→ ⚠ C:\…" greyed and "→ tone-b-660" with 6 | –, 2 | –, 0 | –, 2 | –, Equal-power | –,
      "fade-out at 6 s for 2 s, fade-in from 0 s over 2 s, equal-power", Preview and Preview
      global on each row; the same manager window reused.
    - ✅ Preview on tone-b-660 with faderio stopped: the meter 0.300, ▶ and "0:00 / -0:00"
      unchanged, Stop preview live, "Pre-roll · outgoing 0:02" then 0:03; Stop preview: 0.000,
      the line blank, the button greyed. (A first attempt found the 12 s preview already over.)
    - ✅ A double-click on the tone-b row: the editor page "tone-a-440 → tone-b-660" with
      6 / 2 / 0 / 2 under Close editor, the lists hidden, MusicBee's window the foreground,
      the manager still open.
    - ✅ A double-click on the gone row: "tone-a-440 → tone-c-880", "tone-c-880.wav is
      missing: this transition can be removed, not changed." in red, 5 / 1 read-only, Save,
      Revert, Preview, Render and Capture greyed, Remove transition live. Remove transition:
      the strip "Removed tone-a-440 → tone-c-880." with **1 notice** and no box; the manager
      one row (→ tone-b-660) and two picker items; `transitions.json` back to the a → b it
      held before the run.
    - ✅ Esc hides the manager; Close editor brings the lists back, ✎ on row 1 still bold.
    - ⬜ Not run: the gone row's tooltip (a hover; the headless test reads it), Preview global
      (no override here, so the same transition), and the main-panel layout. The Sounds half
      is the user's.
    - The smoke library's store and MusicBee's ini are as found; `Plugins\mb_faderio.dll` is
      this build.
  - **Review (2026-10-01).** Standards: no hard breach; the one-selected-row read was
    repeated in the view (▶ and Transitions…), now `SingleSelectedRow` as in `MainForm`; the
    first cut of that captured a `RowId?` in a lambda, which `LoadingTests` caught (a closure
    class with a Core value type, as issue 10 found), so `SelectedTrack` is a loop. Left,
    as the issue 11 review left them: the view's manager wiring mirroring `MainForm`'s (the
    notice list's accepted shape), and the test helpers (`OpenManager`, `GridRow`, `Click`)
    repeated across the three WinForms test projects. Spec: everything asked for is present;
    nothing wrong, nothing beyond it but the shell tests' `Click` helper.
  - **Counts:** Core net8.0 855, Core net48 x86 806, Shell net8.0 95, Shell net48 x86 95, App
    187, MusicBee net48 x86 51; 0 failed.
