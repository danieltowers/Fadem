# 10: The plugin view: playlists, queue and transport

Status: resolved
Blocked by: 05, 09

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decisions 5 and 7,
ADR 0012). Staged at the verdict; specified below (2026-09-30) before it was built.

## What to build

A plugin view or dockable panel in MusicBee, composed from the shared controls, showing the
library's playlists, the open playlist's queue and faderio's own transport. It replaces the
spike's two Tools menu items as the way to play. Edits are written back to the library.
faderio playlists are not listed in MusicBee's playlist tree.

Carried from the spike:

- The spike shows notices as message boxes, which are modal and stack. The view gives notices
  a place, as standalone faderio's strip does.
- The spike has nowhere to show the output mode in use (see [13](13-the-output-device-setting.md)).
- Choosing or switching the open playlist inside MusicBee arrives here.

## Implementation decisions

- **A dockable panel, MusicBee's way.** The plugin gains `OnDockablePanelCreated(Control panel)`,
  whose presence tells MusicBee the plugin has a panel element. The user places it once through
  **View → Arrange Panels** (ticking it puts it in the right sidebar), and MusicBee makes the
  panel at each start, calling this with it. The plugin fills the panel with its view, docked to
  fill, and returns −1: MusicBee sizes the view to the panel. `TargetApplication` is "faderio",
  the header MusicBee draws for it. MusicBee disposes the panel when the user removes it from the
  layout; the view goes with it and the engine does not. The plugin type is `PanelView`, as
  panel plugins declare; the settings dialog and the menu work as before on it.
- **MusicBee's start is on a worker thread** (found in the smoke run). At start MusicBee calls
  `Initialise` and `OnDockablePanelCreated` on a worker thread, with a panel the UI thread
  made; only a panel placed while running arrives on the UI thread. So the view is made through
  `panel.Invoke`, on the panel's own thread, and publications post to the UI thread through
  MusicBee's main window (`MainWindowContext`, by the handle the API gives), never through the
  context current at `Initialise`, which is not the UI thread's. `Application.Idle`, the
  thread's own event, is hooked on the UI thread when the panel is filled.
- **The view fits a sidebar.** MusicBee's sidebar is 284 px as shipped. The transport is two
  rows, the buttons sized to fit one row there and wrapping below it; under 560 px the playlists
  sit above the queue instead of beside it; the queue's Artist column gives way to a floor before
  the list scrolls sideways; the host line is cut to half the strip with its whole text as its
  tooltip.
- **The view, `PluginView`, is the plugin's own**, composed from the shared controls as the brief's
  decision 10 says, in `src/Faderio.MusicBee`: a `UserControl` whose fields are reference types,
  so MusicBee's reflection over the assembly still loads it without Core (LoadingTests).
  - **Left**: the shared `PlaylistSidebar`. A click opens a playlist; its menu makes, renames and
    deletes one. Each is one engine call, and the engine writes the library.
  - **Centre**: the queue, a list with #, Title, Artist and Length (the Presenter's cells), the
    attended row marked as standalone faderio marks it. A double-click or Enter plays that row.
    Its menu has Remove and Clear queue, one engine call each. No column layout, no drag: the
    App's queue has those and they can follow when a later issue needs them.
  - **Above the transport**: the attended track's name, as standalone faderio names it.
  - **The transport**: ⏮, ▶/⏸, ⏹, ⏭, ⤳, shuffle, repeat, −5 s, the shared `SeekBar`, +5 s and the
    time readout, each wired to the engine command standalone faderio wires it to. ⏹ is new to
    this host: a guest in MusicBee's process needs a Stop that lets go of the device (issue 06).
  - **The strip**, at the bottom: the latest notice and a count that opens the session's notice
    list, as standalone faderio's strip does, and at its right end the library open and the
    output mode in use with its rate: "library · WASAPI shared at 48000 Hz".
- **Starting sound stops MusicBee's player first.** ▶ from stopped or paused, and a row's
  double-click, call MusicBee's `Player_Stop` before their one engine call, as the Tools menu's
  Play did. ⏸, ⏹, ⏮, ⏭ and seeks do not: they start nothing.
- **Notices have one route.** The plugin's sink is the shell's `PostingSink`, moved from the App
  into `Faderio.Shell` so both hosts marshal publications to their UI thread the same way; the
  plugin hooks MusicBee's `Application.Idle` for its coalesced renders as `Program` does. Every
  notice, the engine's and the plugin's own ("no library is chosen"), goes into a `NoticeLog` the
  plugin's composition holds across panels. While a view is attached it shows the log's latest
  and its count, and no box is shown. With no view attached (the panel not placed, or removed),
  a notice is a message box, as in the spike, so faderio never fails silently. The start-up
  failure is always a box: there is no engine to attach a view to.
- **The Tools → faderio items stay**, Play and Stop, as commands a MusicBee hotkey can be bound
  to. The view is the way to play; the menu is no longer the only one.
- **Moved into the shared library** beside `PostingSink`: `NoticeListForm` and its `SpringLabel`,
  and the attended row's resting colour (`RowMarks`), so the plugin's queue marks rows as the
  App's does. The Presenter gains `FmtHost`, the "library · output" line, which the App's title
  already composed inline.
- **Placement.** The view renders `PlayerView`s and issues commands. It reads the engine's view at
  a click and forgets it. Nothing in it computes from an engine value: every string is the
  Presenter's, every position the engine's.

## Testing decisions

- **Seam 2, the plugin through its own entry point**, extended: `PluginViewTests` in
  `Faderio.MusicBee.Tests` (net48 x86, on an STA thread) hands `OnDockablePanelCreated` a real
  `Panel` and drives what it put there, against the fake output and the TestTemp library:
  - the panel holds the view, sized to fill, and the view lists the library's playlists and the
    open playlist's rows;
  - ▶ calls `Player_Stop` before the output pulls a frame, then the output plays; ⏸ pauses; ⏹
    stops the output; ⏭ moves to the next row; a seek through the bar moves the playhead;
  - a row's double-click plays that row after `Player_Stop`;
  - a notice while the view is attached shows in its strip, and no message box is posted; with
    the panel disposed, the next notice is a box again;
  - the strip's host line names the library and the mode the output opened in.
  MusicBee's UI thread is the tests' `RecordingContext`, which now keeps each post and runs them
  on request, so what reaches the strip went through the sink's post as it does in MusicBee.
- **The shared library's tests** take `PostingSinkTests` from the App tests, running them on
  both targets, and the Presenter's `FmtHost`.
- **Seam 3, the MusicBee Looks half** of `docs/smoke.md`, gains the view: the panel element,
  placing it, what it shows, each transport button, a playlist opened from it, and a notice in
  its strip. The Sounds half is the user's.

## Acceptance criteria

- [x] The listener can open a playlist, play, pause, stop, skip and seek from the view.
- [x] The view holds no engine state and computes nothing from engine values.
- [x] The MusicBee Looks smoke covers the view, item by item.

## Comments

- **Resolved 2026-10-01.** Built as specified above, with the two findings of the smoke run
  folded in (the worker-thread start and the sidebar's width).
  - **Moved into `Faderio.Shell`:** `PostingSink`, `NoticeListForm` with `SpringLabel`, and the
    new `RowMarks`; `PostingSinkTests` moved to the shell's tests and run on both targets;
    `Presenter.FmtHost` carries the "library · output" line the title composed inline.
  - **The plugin:** `PluginView`, `MainWindowContext`, `Host.AttachView` and the notice route
    (log always; strips while a view is attached, a box otherwise), `OnDockablePanelCreated`.
    `Host` captures the fake's mode boxed, since a closure over a Core enum would put a Core
    value type in a closure class MusicBee reflects over before the resolver exists.
  - **Tests:** `PluginTests.View` (nine: the panel's contents and host line, the sidebar and
    main-panel layouts, play/pause/stop with `Player_Stop` first, a row's double-click, skip
    and seek, the strip versus the box with and without a view, a new panel showing earlier
    notices, the worker-thread start, the main-window post route, the panel with no engine);
    `ReleasingTheDeviceTests` gains Stop after a pause and a resume.
  - **The MusicBee Looks half** on the portable MusicBee, WASAPI shared, C-Media at 48 kHz, in
    the right sidebar as ticked in Arrange Panels, then at the width the sidebar was left at:
    - ✅ Starts with no box; the panel is filled at start (after the worker-thread fix) and
      reads as the item describes; at 284 px the seven buttons fit one row, the playlists sit
      above the queue, the columns fit, the host line is cut with its tooltip; wider, whole.
    - ✅ ▶ plays (meter 0.300, name, ⏸, readout and thumb move, row marked); ⏸ pauses (0, ▶);
      ▶ resumes; ⏹ stops: 0, blank name, 0:00 / -0:00, row at rest.
    - ✅ ⏭ to row 2 from 0:00; +5 s → 0:07, −5 s → 0:03; the bar's middle → 0:06 of 0:10;
      ⏮ past 3 s → 0:00 of the same track.
    - ✅ A double-click on row 1 plays tone-a from its start.
    - ✅ A click on Evening opens it: bold, the queue shows tone-b alone.
    - ✅ The exclusive probe evicts the shared stream: the strip reads "Audio output lost
      (Ex…" with "1 notice", no box; the link opens the notice list with the time, text and
      full detail; ▶ plays again; ⏹ stops.
    - ✅ Tools → faderio still reads Play, Stop.
    - ⬜ Not run this time: the device-free probe, Preferences and Configure, the removed
      dependency folder (none of that code changed), and a playlist rename from the view, which
      needs keyboard focus. The Sounds half is the user's.
  - **Review (2026-10-01).** Standards: no hard breach; the one large judgement call is that
    `PluginView` repeats the App's queue list and strip (rows and marks, Remove/Clear/Delete,
    the notice strip and list opening, tips) which a shared `QueueList` and `NoticeStrip` in
    `Faderio.Shell` could absorb: left for issue 11, whose editor button on the rows is the
    App's queue's other half. Two names fixed. Spec: everything asked for is present; the
    seek test now goes through the bar's own mouse handling, not the view's `SeekTo`.
  - **Counts:** Core net8.0 855, Core net48 x86 806, Shell net8.0 92, Shell net48 x86 92, App
    187, MusicBee net48 x86 36; 0 failed.
