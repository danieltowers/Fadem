# 11: The transition editor and preview in MusicBee

Status: resolved
Blocked by: 05, 09, 10

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decisions 1 and
10, ADR 0012). Staged at the verdict; specified below (2026-10-01) before it was built.

## What to build

The shared transition editor panel in the plugin's view: authoring an adjacency's transition,
capture by ear, and preview, as in standalone faderio. Preview plays through the plugin's
output, so it takes and lets go of the device as Play and Stop do.

## Implementation decisions

- **The shared editor, in the view.** `PluginView` composes the shared `TransitionEditorPanel`
  as `MainForm` does, with the same four collaborators: the engine, a notice route, the render
  file dialog and the unsaved-edits question. Where it sits follows the view's width, as the
  playlists do:
  - **In a main panel** (560 px or wider) it docks right at its own width, always shown, as in
    standalone faderio.
  - **In a sidebar** it is a page: hidden until an adjacency opens, then in the lists' place
    (the playlists and the queue hide), filling what is left under the transport and a
    **Close editor** bar, scrolling inside as it does. The bar settles unsaved edits, empties
    the editor and brings the lists back. Resizing across the threshold moves the editor, open
    or not. Decided in the smoke run: MusicBee stacks its sidebar panels, so faderio's can be
    some 330 px tall, where a bottom-docked editor and the lists cannot share the height. The
    transport stays above the page for capture by ear.
  - The editor's button rows wrap in a narrow panel (auto-sized rows) so nothing is cut off at
    284 px; at the App's 340 px nothing wraps, so standalone faderio is unchanged.
- **Opening an adjacency.** The queue gains the App's last column: ✎ on every row but the
  last, bold while this → next is authored, grey and plain otherwise. A click on it opens the
  editor on this → next; a double-click on it is two presses, not a request to play. The queue's
  menu gains **Edit transition: this → next**, **previous → this** and, under repeat-one,
  **this → this**, each one engine call (`AdjacenciesOf`) then the editor's `Open`. The
  transition manager's items wait for issue 12.
- **Preview plays through the plugin's output.** The output takes and lets go of the device on
  its own, as it does for Play and Stop (issue 06): a preview from a stopped or paused queue
  opens the device, and its end stops the output, which closes it. Nothing is added there. What
  the view adds is MusicBee's side: the editor takes a `beforeSounding` hook, raised before the
  engine's `Preview` runs, and the view's calls MusicBee's `Player_Stop` when faderio makes no
  sound now (the queue not playing and no preview running), as ▶ does. A preview restarted over
  a running one, or started over a playing queue, stops nothing: faderio already has the device.
- **The editor's notices have the host's one route.** "Not saved: …", "Not previewed: …" and
  "Rendered …" go into the session's log and every attached view's strip, through `Host`, as the
  engine's notices do; never a box while a view is attached.
- **The unsaved-edits question is the shell's.** `Dialogs.AskUnsavedEdits` in `Faderio.Shell`:
  the task dialog on net8.0-windows, and on net48 a Yes / No / Cancel message box titled
  "Unsaved transition", reading "Save your changes to outgoing → incoming?" and "The editor has
  changes that are not saved. Yes saves them, No discards them." (the brief's TaskDialog swap). `Dialogs.ChooseRenderPath` holds the WAV save dialog, the same on both. `MainForm` uses
  both, so the two hosts ask the same question.
- **Leaving the editor.** The sidebar's playlist switch and new playlist settle the editor
  first and empty it, as in standalone faderio. MusicBee disposes the panel without asking, so a
  draft open when the user removes the panel from the layout is lost; a preview running then
  plays on until Tools → faderio → Stop, which ends it as the engine ends it. Both accepted:
  MusicBee gives the plugin no veto.
- **The row look is shared.** `QueueRowStyle` in `Faderio.Shell` styles one queue row for a
  view (the attended mark, the unreadable grey, the ✎ cell's glyph, weight and colour, the row's
  label and tip) and finds the ✎ under a click; both hosts use it. The App's queue keeps its
  columns, menu and drag, which stay standalone: the whole `QueueList` the issue 10 review
  suggested is not carved out here, and the pencil's other half is the part shared.
- **Placement.** Every string is the Presenter's or the editor's; the view reads the engine's
  view at a click and forgets it. Whether faderio sounds now is read from the published view,
  not computed.

## Testing decisions

- **Seam 2, through the plugin's entry point**, `PluginTests.Editor` (net48 x86, STA), on the
  fake output and the TestTemp library, driving what `OnDockablePanelCreated` put in the panel:
  - ✎ on row 1 opens the editor on row 1 → row 2 with the authored crossfade in its fields; the
    last row shows no ✎; the menu's three items act on the selected row.
  - **The reverse of the acceptance criterion:** a transition authored through Core's store, as
    standalone faderio leaves it, loads into the plugin's editor exactly; and one saved from the
    plugin's editor is read back from the library's store as the spec the fields held, so
    standalone faderio opens it unchanged.
  - **Preview** from a stopped queue calls `Player_Stop` before the output pulls a frame, then
    the output plays; the queue's rows and attended row are as they were (the view's `Preview`
    set, `Transport` the queue's); Stop preview stops the output, with the queue where it was. A
    preview over a playing queue calls `Player_Stop` no second time. A refused draft is a notice
    in the strip and no box.
  - **Layout:** in a sidebar 330 px tall the editor is hidden until ✎, then fills the lists'
    place under the Close bar, with the transport still above it; Close empties it and brings
    the lists back; in a main panel it is docked right from the start, and a resize across the
    threshold moves it either way.
  - A playlist switch with unsaved edits asks through the hook, and Cancel stays.
- **The shared library's tests** (both targets): `QueueRowStyle` on a list item, and the
  editor's button rows wrapping below 300 px and not at 340 px; `Dialogs` are not shown headless.
- **The App's tests** pass unchanged on `MainForm` composed with `QueueRowStyle` and `Dialogs`.
- **Seam 3, the MusicBee Looks half** of `docs/smoke.md`, gains the editor: the ✎ column, the
  editor opening in the sidebar and in a main panel, its fields, a save and its notice in the
  strip, Preview heard on the meter with the queue held, Stop preview, the unsaved-edits
  question, Close. The Sounds half
  gains preview through MusicBee, and the authored-both-ways listening.

## Acceptance criteria

- [x] A transition authored in MusicBee plays the same in standalone faderio, and the reverse.
      (One store, read and written by one engine: the tests read back what the plugin's editor
      saved through Core's store, and load what Core authored into the plugin's editor. The
      listening is the Sounds half's.)
- [x] Preview plays the draft and leaves the queue where it was.
- [x] The MusicBee Looks smoke covers the editor; the Sounds half gains preview.

## Comments

- **Resolved 2026-10-01.** Built as specified above, with one finding of the smoke run folded
  in: the sidebar editor is a page, not a bottom dock (see the layout decision).
  - **The shell:** `QueueRowStyle` (the row look and the ✎ hit test, used by both hosts),
    `EditTransitionMenu` (the queue menu's three Edit transition items, used by both hosts),
    `Dialogs` (the unsaved-edits question per target, the render file dialog), the editor's
    `beforeSounding` hook and its button rows wrapping. `MainForm` composes them; its tests
    pass with only the menu items' names changed. The engine's `PlayerView` gains `IsSounding`,
    which ▶ and the preview hook read instead of deriving it.
  - **The plugin:** `PluginView` composes the editor, with the ✎ column, the three Edit
    transition menu items, the sidebar page with its Close bar, and MusicBee's player stopped
    before a preview sounds when faderio is silent. The editor's notices go through `Host`'s
    one route.
  - **Tests:** `PluginTests.Editor` (nine: the pencil and the loaded fields, preview with
    `Player_Stop` first and the queue held, no second stop over a playing queue or a running
    preview, a refused draft in the strip, the sidebar page and Close, the main panel and the
    resize, the playlist switch asking and Cancel, the saved transition read back from the
    store, the menu's items and this → this under repeat-one); the shell's `QueueRowStyle` and
    the wrapping rows (both targets). Checked by mutation: the Player_Stop guard and the wrap.
  - **The MusicBee Looks half** on the portable MusicBee, WASAPI shared at 48 kHz, in the
    right sidebar as the user left it (376 px wide, about 330 px tall under two other panels),
    then in the main panel by the ini:
    - ✅ The queue's ✎ column: bold on row 1 (authored), none on row 2.
    - ✅ In the sidebar no editor until ✎; then the page "tone-a-440 → tone-b-660" under the
      transport and Close editor, Global ticked, 6 / 2 / 0 / 2 in the fields, − + and Capture
      (greyed while silent), scrolling inside. The wrap at 284 px was not seen at this width
      (the headless test covers it).
    - ✅ "abc" in Fade-out duration, Save: "Not saved: "abc" is nc…" with **1 notice**, no
      box, the reason in red under the field. 3, Save: "Saved tone-a-440 → tone-b-660.",
      **2 notices**, `transitions.json` holds "00:00:03".
    - ✅ Preview: the meter 0.300 at 1.5 s and 3 s; ▶ and "0:00 / -0:00" unchanged; both
      Capture live; "Preview again", Stop preview live, "Pre-roll · outgoing 0:05". Stop
      preview: 0.000, Preview and Stop preview as before. ▶ then plays tone-a-440 ("0:01 /
      -0:09"), ⏹ stops: 0.000.
    - ✅ 4 typed, Close editor: "Unsaved transition" box, "Save your changes to tone-a-440 →
      tone-b-660?" with Yes / No / Cancel; closed (Cancel): the editor stays with 4. Revert,
      Close editor: the lists come back, row 1's ✎ still bold.
    - ✅ In the main panel: the editor at the right from the start, "No boundary selected",
      fields greyed, no Close bar; ✎ fills it with 6 / 3 / 0 / 2 and Equal-power; 4 typed and
      a click on Evening asks the same box; Cancel keeps Untitled open and the draft.
    - ⬜ Not run: Render (a file dialog), capture by ear mid-play, and the playlist layer.
      The Sounds half is the user's: preview heard as standalone's, and the authored-both-ways
      listening.
    - The ini and the smoke library's `transitions.json` were put back as found.
  - **Review (2026-10-01).** Standards: the "is faderio sounding" test was derived in the shell
    from two engine values, in two shapes (▶ and the preview hook); now one `PlayerView`
    member, read by both. The Edit transition items were a verbatim copy of `MainForm`'s;
    now `EditTransitionMenu` in the shell. The editor owns its design width; the row loop is
    the style's; "boundary" in a new comment became "adjacency"; missing doc comments added.
    Left: `MainForm.EditButtonAt` forwarding to the style (the tests' entry point), and the
    menu-opening test helper repeated across the three test projects. Spec: everything asked
    for is present; the menu test now clicks this → next too, and the resize test crosses the
    threshold with the editor closed as well as open.
  - **Counts:** Core net8.0 855, Core net48 x86 806, Shell net8.0 94, Shell net48 x86 94, App
    187, MusicBee net48 x86 45; 0 failed.
