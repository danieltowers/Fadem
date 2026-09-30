# 09: The shared WinForms library

Status: resolved
Blocked by: 05

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 10, ADR
0012). Staged at the verdict; it needs its own spec before an agent builds it.

## What to build

`Faderio.Shell` (multi-targeted `net8.0-windows` and `net48`) holds only the audio output
today. The shell's reusable parts move into it from the App: the Presenter, the transition
editor panel, the transition manager, the playlist sidebar and the seek bar. `MainForm` and
`Program` stay standalone-only.

Known from the brief's audit: shell reuse needs four API swaps on net48 (`TaskDialog`,
`PlaceholderText`, the folder dialog and `Program`). Fitting MusicBee's window and message loop
matters more than the swaps.

The moved code stays a shell under `CLAUDE.md`'s three placement tests.

## Acceptance criteria

- [x] The App builds on the shared library and standalone faderio behaves as before: its suite
      and the Looks half of `docs/smoke.md` pass.
- [x] The shared library builds for net48 and its controls construct headless on net48 x86.
- [x] The commit states net8.0 and net48 x86 counts.

## Comments

- **Resolved 2026-09-30.** Seven files moved from the App into `Faderio.Shell`, namespace
  `Faderio.Shell`, unchanged in behaviour: `Presenter`, `TransitionEditorPanel`,
  `TransitionManagerForm`, `PlaylistSidebar`, `SeekBar`, and the two the Presenter needs,
  `ColumnLayout` and `NoticeLog`. `MainForm`, `Program`, `SettingsForm`, `NoticeListForm`,
  `PlaylistNameBar`, `PostingSink` and `WindowGeometry` stay in the App. The shell library now
  targets `net8.0-windows;net48` with WinForms, and its internals are visible to both hosts
  and their tests (the App's assembly is named `faderio`).
  - **The net48 swaps**, in the shell's own `Compat` as Core's: `Placeholders.Get/Set` for
    `TextBox.PlaceholderText`, drawn on net48 by the edit control's cue banner
    (`EM_SETCUEBANNER`, shown while empty whether or not focused); `ShellCompat.Clamp`,
    `IsDefined<T>`, `Values<T>`; a `KeyValuePair` deconstruct; an `IsExternalInit`; and
    ranges and `TimeSpan * int` rewritten at their call sites. `TaskDialog` and the folder
    dialog are `MainForm`'s and did not move, so the brief's other two swaps wait for the
    plugin view (issue 10).
  - **Tests.** New `Faderio.Shell.Tests` (net8.0-windows and net48 x86) takes
    `PresenterTests`, `SeekBarTests` and `ColumnLayoutTests` from the App tests (81) and adds
    `HeadlessControlsTests` (4): the sidebar, the editor with its blank-duration placeholder,
    the manager and the seek bar constructed on an STA thread against a real engine on the
    fake output, never shown. 85 on each target. Core's internals are visible to it too.
  - **The standalone Looks half**, on the moved controls, in the built App with two generated
    20 s WAVs in a fresh library and machine folder, WASAPI shared at 48000 Hz:
    - ✅ Opens without an error dialog; the title reads as specified; playback runs 20 s + 20 s.
    - ✅ The editor at launch reads "No boundary selected", every field, From end, the curve
      and both radio buttons greyed; ✎ on row 1 opens outgoing → incoming and makes them live.
    - ✅ Title fully visible; all four fields, both Capture buttons, From end and Fade curve in
      view without scrolling; Save, Revert, Remove transition and Render visible and enabled.
    - ✅ Empty duration boxes show "5" in grey (the blank duration); the playlist layer shows
      the global's 3.6 in grey, the curve "(global)", and Remove override beside Render.
    - ✅ Typing 3.5 enables − and +; + gives 3.6; From end on a 20 s track shows 16.4, and
      unticked 3.6 again.
    - ✅ A fade-out point of 25 is refused: "Not saved: …" in the strip, the reason in red under
      the field, readable in full. Render is then partly below the window edge at the default
      size, in this build and in `47eb005` alike (captured both): pre-existing, not this move's.
    - ✅ Save says "Saved outgoing → incoming." and row 1's ✎ turns bold; a second Save reads
      "×2". Remove transition says "Removed …" and leaves the fields filled; Save puts it back;
      Revert reloads 3.6 over a typed 9.
    - ✅ Switching layer with unsaved edits asks Save / Discard / Cancel, "Save your changes to
      outgoing → incoming?"; Cancel stays.
    - ✅ Transitions… opens "faderio — transitions" on outgoing: the legend, the → incoming row
      reading "3.6 | –" and "Equal-power | –", the effective text, Preview and Preview global,
      Stop preview greyed. The close box hides it.
    - ✅ The Playlists panel lists "Untitled" bold on yellow; right-click on empty space offers
      New playlist with Rename and Delete greyed; New playlist adds "Untitled 2", highlighted,
      and empties the queue and the editor; clicking "Untitled" brings its rows back, and the
      empty Untitled 2 is dropped, as the engine says it should be.
    - ✅ The seek bar has full travel and the transport row reads as specified; the thumb
      moved with playback in the timed run.
    - ⬜ Not run: the items on MainForm, columns, notices, drag and drop, imports, settings,
      device fallback, window geometry and the long FLAC. None of that code moved.
  - **Seen, not this issue's:** `--edit-first` without `--play` left the editor on "No
    boundary selected" on two launches (the queue list had not rendered its rows when
    `EditTransitionAt(0)` ran); the first launch with `--play --edit-first` opened it. In
    `Program.cs`, untouched here.
- **Counts:** Core net8.0 855, Core net48 x86 806, Shell net8.0 85, Shell net48 x86 85, App 193,
  MusicBee net48 x86 24; 0 failed.
