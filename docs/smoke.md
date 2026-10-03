# Smoke checklist

Run by **every session that touches the shell**, before that session's commit. Decided in
[wayfinder ticket 06](../.scratch/202609-faderio-v3/issues/06-how-do-we-make-green-tests-and-a-broken-app-impossible.md).

The list is in two halves, each run by the party that can actually judge it. A session never
reports "smoke passed" on items it could not perceive.

Scoped to the slice as defined in
[ticket 05](../.scratch/202609-faderio-v3/issues/05-what-exactly-is-in-the-narrow-slice.md).
Extend it when the shell grows; never let an item go stale.

## Looks — the agent runs this

Launch the app, take a screenshot, and check each item against it. Report the result item by
item in the session's hand-off. Plain arguments are files to queue; `--play` starts them and
`--edit-first` opens the editor on the first boundary;
`--unavailable=exclusive,shared` treats those modes as if their device were taken, to force the
fallback chain; `--library=<folder>` opens that folder as the library without asking;
`--machine=<folder>` keeps this machine's `settings.json` and `window.json` in that folder
instead of `%APPDATA%\faderio`. Point both at fresh temp folders, so a smoke run writes nothing
into the user's library or machine settings. `--library=` without `--machine=` keeps no machine
files at all: the output mode is the default, nothing is remembered, and the window opens
centred. The output mode is a machine setting, chosen in **Settings…** and used from the next
launch, so the items that relaunch pass the same two folders again:

```
dotnet run --project src/Faderio.App -- "<outgoing>" "<incoming>" --play --edit-first --library="<temp library>" --machine="<temp machine>"
```

`<outgoing>` and `<incoming>` are **absolute paths to two real, promised files**; there are no
sample files in the repo. `dotnet run` runs the app from `src/Faderio.App`, so a relative path
resolves there, not where you typed the command. A path that does not exist is rejected like
any other file: nothing is queued and the only sign is one line in the status strip
("Nothing added: … The file does not exist."). The agent generates its own WAVs for the Looks
half; the Sounds half uses the two tracks of a boundary already in the store
(`<library>\.faderio\transitions.json` lists them by full path). Everything the app writes for a
library is under its `.faderio\`. `%APPDATA%\faderio\` holds only what is the machine's:
`settings.json` (the output mode, and the library a plain launch opens) and `window.json` (where
the window was closed).

Capture the window with `PrintWindow` rather than a screen grab, so it need not be in front.
There is no ⏹ button (the engine still stops at launch and when the queue ends). UI Automation
folds some symbol glyphs together, so find a transport button by comparing names ordinally, not
with a name condition. The status strip's items are not exposed to it; read them
from a capture. The seek bar is a pane named "Seek" whose thumb is not an element of its own: find
the thumb in a capture (the highlight colour), and click it with posted mouse messages. The
queue's ✎ button is different: the list answers a click where the real cursor is, not where a
posted click says, so move the cursor onto the cell (`SetCursorPos`) before posting the click.

- [ ] The window opens without an error dialog.
- [ ] The title reads "Untitled — faderio — *temp folder's name* · WASAPI shared at 44100 Hz"
      (or the mode in use), and nothing asked for a folder. **Open library…** sits between
      **Export playlist…** and **Settings…**; choosing another folder in it empties the queue and
      the editor and puts that folder's name in the title.
- [ ] Above the queue, beside the sidebar, the open playlist's name reads in a larger font, and
      the title bar starts with the same name. Double-click it, or press F2 in the queue: it
      becomes a text box holding the name, all selected. Enter renames it there, in the sidebar
      and in the title; Esc puts the old name back. A name another playlist has is refused with
      "Not renamed: …" in the strip, and the old name stays.
- [ ] Add-files puts both tracks in the list, each with a title (or filename) and no blank row.
- [ ] The transport row reads ⏮ ▶ ⏭ ⤳ 🔀 🔁, a **Sync editor** checkbox, then −5 s, the seek
      bar, +5 s and the time; no ⏹.
- [ ] Between the buttons and the transport row, a line names the playing track in a larger
      font: its title and artist ("Title — Artist"), or its filename when untagged. It is blank
      at launch, keeps naming the track while paused, and goes blank when the queue is cleared or
      runs out. The transport does not move when it fills or empties. A name too long for the
      window ends in "…" and shows whole in its tooltip.
- [ ] At launch the first row carries the marker, dimmed (a greyer yellow than while playing).
      Playing or paused, the marker is the full pale yellow. When the queue runs out it goes
      back to the first row, dimmed, and ▶ plays that row.
- [ ] Pause, click a different row, press ▶: that row plays from 0:00 and carries the marker.
      With the playing row, no row or several rows selected, ▶ resumes where it paused.
- [ ] The seek bar has visible travel across the width of the transport area, not a stub.
- [ ] While playing, the seek thumb moves smoothly — not in visible steps.
- [ ] Clicking three-quarters along the seek bar jumps the thumb there and the readout to about
      three-quarters of the length, playing or paused. Dragging the thumb moves it without
      moving the sound; letting go seeks there. At launch the bar is a plain grey groove with no
      thumb, and clicking it does nothing.
- [ ] **Dragging the fades** (phase 6 issue 11). Playing a row with an authored fade-out (and a
      fade-in from the row before), the editor open on its this → next: the cursor over a red
      mark, above or below the groove, is ↔; over a grey wedge there it is the move cursor; on
      the groove it stays the arrow. Dragging the fade-out's start mark left, the mark and its
      wedge follow the mouse and the thumb stays put; on release the strip says "Saved …", the
      wedge ends where it ended before, and the editor's fade-out point and duration show the
      new values at once. Dragging the wedge right slides it whole; the editor's point moves,
      the duration does not. A click on the groove under a mark still seeks.
- [ ] **+5 s** twice moves the readout on ten seconds and **−5 s** back five, playing or paused;
      near the start −5 s stops at 0:00. Both are greyed out at launch, with the seek bar.
- [ ] The time readout shows elapsed and remaining, and remaining never reads `0:00` mid-track.
- [ ] Right-clicking a row shows "Edit transition: this → next" and "Edit transition: previous
      → this", and, only while repeat is on one, "Edit transition: this → this". All read
      clearly, and a disabled one looks disabled. With repeat off, this → next is disabled on
      the last row and previous → this on the first. Under repeat-all, this → next on the last
      row opens last → first. This → this opens the row into itself.
- [ ] At the far right of every row but the last sits ✎: bold and black where that row into the
      next has a saved transition, plain and grey where it has none. The last row has none, even
      under repeat-all. Clicking it opens the editor on this → next, as the menu does: the
      heading changes to that adjacency. Saving or removing the transition flips its row's ✎.
      At the 960 px minimum width it is still in view, with no sideways scroll bar.
- [ ] A fresh library shows the queue's default four columns: #, Title, Artist, Length. A
      right-click on the header (not on a row) shows a checklist of #, Title, Artist, Album,
      Length, Format, Replay gain and Path, with # ticked and greyed out. Ticking Path adds a Path
      column showing each row's full path; ticking Album, Format and Replay gain shows, for a
      tagged FLAC, its album, "44.1 kHz / 16-bit" and "−7.2 dB" (blank where untagged). ✎ stays
      the rightmost column. With Album, Format, Replay gain and Path ticked at the default size,
      ✎ is still in view with no sideways scroll bar: Path, Album and Artist give way, in that
      order. Widening the window gives Artist, then Album, then Path their widths back. Only at
      the 960 px minimum with all eight ticked does the list scroll sideways to ✎. Dragging a header moves it (never before # or after ✎), and a dragged width holds.
      A right-click on a row still shows the row's menu. Relaunched, or after **Open library…** elsewhere and back, the
      layout is as it was left; the other library shows its own. The file is
      `<library>\.faderio\columns.json`.
- [ ] The editor saves itself (phase 6 issue 05): a box typed in saves on Tab, a click
      elsewhere or Enter, with "Saved *a* → *b*." in the strip; capture, − / +, **From end** and
      the curve save at once. Opening another adjacency, closing the window, switching playlist
      or library never asks: a box being typed in is saved first. Text that cannot be saved
      ("abc", or a point past the end) shows its reason and is dropped on leaving.
- [ ] The transition editor opens with its title fully visible — not clipped, not behind
      another control.
- [ ] All four parameter fields, the capture buttons, **From end** beside the fade-out point
      and the **Fade curve** list are visible without scrolling at the default window size.
- [ ] Undo, Redo, Remove transition and Render are visible once the editor is open on an
      adjacency; there is no Save and no Revert. Undo and Redo are greyed until there is a
      change to walk, and their tooltips say which ("Undo: saved *a* → *b*"). Undo puts the
      field and the store back, opening the adjacency it changed if another is open; Redo puts
      it back again. Ctrl+Z and Ctrl+Y do the same with focus anywhere in the editor; Ctrl+Z in
      a box being typed in puts back what it held before the typing.
- [ ] With the editor open, an empty duration box shows in grey what it plays if left blank:
      the **Blank fade durations** setting (5 by default) in Global, and in This playlist the
      global's duration, or that setting where the global sets none. Typing over it shows the
      typed value in black. Change the setting in **Settings…** with the editor open: the grey
      value follows at once.
- [ ] With no boundary open (at launch, and after **Open library…**), the editor reads "No
      boundary selected" and every field, **From end**, the curve, ● Global and ○ This playlist
      are greyed out. Opening a boundary makes them live.
- [ ] Ticking **From end** on a fade-out point of 3.5 on a 5 s track shows 1.5; unticking shows
      3.5 again. The fade curve lists Equal-power and Linear.
- [ ] Saving a fade-out point past the end of its track stores nothing: the strip says "Not
      saved", and the reason shows in red under the field, readable in full, without pushing
      the buttons out of sight. The next good value clears it.
- [ ] At the top of the editor, "Editing: ● Global ○ This playlist (*name*)". Switched to This
      playlist, the boxes are empty with the global values in grey, the curve reads "(global)",
      and **Remove override** sits under Remove transition, beside Render; Render stays in view
      at the default window size. Typing one value shows it black among the grey; leaving the
      box says "Saved *a* → *b* in *name*." and a grey line under the selector names what the
      playlist plays. **Remove override** says "Removed the override of …" and the line goes.
      Switching mode saves a box being typed in to the mode left; the next ✎ opens in the last
      mode.
- [ ] **Transitions…**, between **Export playlist…** and **Open library…**, opens a separate
      "faderio — transitions" window on the selected row's track (else the first track with a
      transition), listing the boundary authored during the run: ← or →, the other track, then
      each parameter and the curve as "global | override", "–" where a layer sets none, the
      effective transition and a playlist-only ✓. A value saved in the editor while it is open
      appears in it. Each row has **Preview** and **Preview global**; either starts a preview,
      its line ("Pre-roll · outgoing 0:05") beside **Stop preview**, which ends it. A double-click
      on a row opens it in the editor, in the editor's mode. The queue's right-click menu has
      "Transitions for this track…", opening it on that row's track. Esc and the close box hide it.
      Author a transition, rename one of its files and open the manager: the row reads
      "⚠ *path*", greyed, with a tooltip saying it was not found where the store says; the picker
      marks that track the same way. Double-click it: under the heading the editor says
      "*file* is missing: this transition can be removed, not changed."; Preview, Render, capture
      and ± are greyed and the boxes read-only; Remove transition removes it and the row leaves the manager.
- [ ] **Remove transition** says "Removed …" in the strip and empties the fields; **Undo**
      puts the transition back, "Undone: removed …".
- [ ] Pressing capture writes a number into the field, and it is the number the time readout
      showed at that moment.
- [ ] Capture reads the open transition's own tracks (phase 6 issue 06). Track a playing, the
      editor on b → c: both Capture buttons greyed, the grey line under the fade-out point reads
      "Play *b* or preview to capture", under the fade-in point the same for *c*. Play b: the
      fade-out Capture comes live, its line goes, its tooltip reads "Capture *b*'s playhead";
      the fade-in one stays greyed. A preview of b → c makes both live.
- [ ] The traffic light (phase 6 issue 07) sits at the editor's top right, on the heading's
      line, hidden with no boundary open. Editor on b → c: grey when stopped, red while a plays,
      green while b plays, amber while c plays. Its tooltip says which ("Playing *b*, this
      transition's first track").
- [ ] Beside each **point** box (not the durations) sit **−** and **+**. Each press moves the
      number shown by 0.1, Shift-click by 1; with **From end** ticked, + makes the from-end number
      bigger. They stop at 0 and at the track's length, and are greyed out while the box is blank
      or not a number.
- [ ] **Preview** and **Stop preview** sit together in the editor, Stop preview greyed out
      until a preview runs. While one runs, Preview reads **Preview again**, and the line under
      them shows the phase and each voice's position ("Pre-roll · outgoing 2:25", then
      "Transition · outgoing 2:31 · incoming 0:12", then "Tail · incoming 0:20"), moving as it
      plays. The main window's readout reads "Held 1:10 / -2:50" and stands still, and the play
      button shows ▶. When the preview ends, the line clears and the readout loses "Held".
- [ ] Resizing the window narrower does not hide the transport or the editor's buttons.
- [ ] While adding a folder's worth of files (fifty or more, through **Add files…**, **Add
      folder…** or as arguments, and once the whole `MUSIC` library), the window stays
      responsive — it moves and repaints — and
      the status strip shows *Adding N of M…* and a **Cancel**. Rows appear in order as files resolve. Both
      disappear when the add ends, and Cancel ends it at once, keeping the rows already listed.
- [ ] **Add folder…** on a folder of several albums
      lists the albums in year order, each album's discs in order, and each album's tracks
      "1" to "10" in natural order. The cover scans are ignored without a notice.
- [ ] Dropping a mix of files and folders from Explorer onto the list adds them in the order
      dropped, each folder expanded in place: dragged over the rows they show the drop line and
      land on it (phase 6 issue 03), and below the rows at the end.
- [ ] **Export playlist…** offers "*open playlist*.m3u8"; saved in a folder whose name has `†`
      or `⭐️`, the strip names the file saved. **Import playlist…** on that file (it takes
      several, and only `.m3u`/`.m3u8`) adds it to the sidebar as "*name* 2", open, with the same
      rows in the same order; the strip says "Imported *file* as *name* 2.". A `.m3u` of relative
      paths from another folder imports with its paths made absolute in
      `<library>\.faderio\playlists\`.
- [ ] Ctrl-click or Shift-click selects several rows. **Delete** removes them all at once and
      the numbering closes up. The context menu's **Remove** does the same, and is disabled
      when nothing is selected.
- [ ] **Clear queue** in the context menu, with more than one row, shows a confirmation.
      Cancel leaves the queue as it was; OK empties it and playback stops.
- [ ] Dragging selected rows shows a line where they will land, and dropping moves them there,
      still selected and in their order. In a queue longer than the list, a drag held at the
      bottom edge scrolls it.
- [ ] While playing on WASAPI shared, **Delete** on the only row and **Clear queue** each
      stop playback, and the window keeps rendering afterwards: the play button
      shows ▶ and the readout resets. Repeat each a few times; the freeze was a race.
- [ ] Beside ⏭, the shuffle toggle (🔀) and the repeat button read clearly in every state:
      each shows pressed in while on and plain while off. Repeat cycles off (🔁, plain) → all
      (🔁, pressed in) → one (🔁1, pressed in) → off, and each tooltip names its state.
- [ ] Between ⏭ and the shuffle toggle, the Transition now button (⤳) reads clearly and its
      tooltip says "Transition now". It is greyed out while stopped, while paused, on the last
      row with repeat off, and during a crossfade, and live otherwise.
- [ ] **Sync editor** is unticked in a fresh library, and its tooltip says what it does. Ticked
      while a row plays, the editor opens on that row's this → next at once; when the next
      track comes in (once the outgoing one falls silent) it moves to that track's this → next.
      On the last row it stays where it was. Opened on another boundary between tracks, the
      editor stays there until the next track comes in. The tick survives a relaunch.
- [ ] The status strip shows the latest notice and, at its right, a count ("3 notices").
      Saving the same boundary several times shows one "Saved … ×n", not n notices.
      A notice longer than the strip is clipped and never pushes the count off. Clicking the
      notice or the count opens the notice list: every notice this session, oldest first, with
      its time and its count, and the selected one in full below. Esc and the close box hide
      it, and clicking the strip again brings back the same list.
- [ ] At the strip's far right, the clip light reads "● No clips", dark and greyed, by default.
      Lit, it reads "● 2 clips · latest: A → B" in white on red, legible at a glance, and a
      long cause is cut short without pushing the notice count off; its tooltip has it whole.
      Clicking it turns it dark again. (A track tagged well above 0 dB, with ReplayGain on,
      lights it.)
- [ ] Launching with a corrupt `<library>\.faderio\transitions.json` shows
      no dialog: the strip says the file was moved aside to `transitions.json.corrupt`.
- [ ] A queued file renamed before the track ahead of it starts is marked when that track starts:
      "⚠" before its row number, the row greyed, and a tooltip saying it is unreadable. The
      strip says it was passed over, once, and the queue plays on past it. (The file is opened
      as the track before it starts, so renaming it later still plays it.)
- [ ] Adding a rejected file (an `.m4a`, an `.ogg`, or a file with more than two channels)
      leaves the list unchanged and shows one notice naming the property that failed and
      pointing at `docs/supported-audio.md`. Nothing is queued, nothing plays.
- [ ] With WASAPI exclusive chosen in **Settings…**, launched with `--unavailable=exclusive`,
      the title names WASAPI shared, not exclusive, and the strip says exclusive was
      unavailable. With `--unavailable=exclusive,shared` the title names WaveOut at 44100 Hz,
      with a notice for each step.
- [ ] **Settings…** opens a dialog with OK and Cancel showing the four settings in two
      groups. **This machine**: the output mode with "Applies at the next launch." under it.
      **This library**: *Hard cut* or *Crossfade for N s*, with N greyed out under *Hard cut*;
      **Blank fade durations** in seconds (0 to 30, live under either choice); and ReplayGain. Launched into a fallback as above, it also reads "In use now: WASAPI
      shared" (or WaveOut) under the mode chosen.
- [ ] A setting changed and saved with OK is still set after a restart; Cancel keeps nothing.
      Changing only the output mode says "Saved machine settings."; only a library setting,
      "Saved settings.".
- [ ] Crossfade for 12 s in one library, then **Open library…** on a fresh folder: **Settings…**
      shows *Hard cut* there, and the output mode chosen before. Back to the first library:
      12 s again.
- [ ] Down the left, between the transport row and the status strip, a ~200 px **Playlists**
      panel lists the library's playlists, the open one bold on the marker's yellow. In a fresh
      temp library launched with two files it shows "Untitled", highlighted. Right-click on empty
      space offers **New playlist** (Rename and Delete greyed out); it adds "Untitled 2",
      highlighted, and empties the queue and the editor.
- [ ] Clicking a playlist opens it: its rows replace the queue and the highlight moves. A box
      being typed in in the editor is saved first, with no question.
- [ ] F2, a double-click or the menu's **Rename** edits the name in place; Enter renames the file,
      Esc leaves it. A name another playlist has is refused: the strip says "Not renamed: …"
      and the label shows the old name. A slow second click on a selected name starts nothing.
- [ ] **Delete** asks once, Yes/No with No the default, naming the playlist. No keeps it. Yes on
      the open playlist removes its file and opens the first remaining one.
- [ ] A playlist file copied into `<library>\.faderio\playlists\` from outside appears at the
      next playlist command (open, new, rename, delete) or library open, not on its own.
- [ ] Resize and move the window, close it, relaunch with the same `--machine=`: it opens in
      the same place at the same size. Closed maximised, it opens maximised.
- [ ] Launching with a corrupt `<library>\.faderio\settings.json` shows no
      dialog: the strip says the file was moved aside to `settings.json.corrupt`, and
      **Settings…** shows the library's defaults.
- [ ] Adding the library's longest FLAC (about 26 minutes) shows its full length in the time
      readout, and seeking into its last minute lands there. Length is not proven by the test
      suite; this is where it is checked.
- [ ] **Sections** (phase 6 issue 09). With four rows and none selected, the queue menu's
      **Section** is greyed out. On the second row, **Section → Start a section here…** asks
      "Section name:" in a small dialog; "Intro" and OK shows a header "Intro" behind a swatch of its
      colour, with a chevron, above rows 2–4, row 1 under a blank header, and a stripe of that
      colour at the left of rows 2–4 only.
      Starting "End" on row 4 heads it in another colour. **Rename section…** opens on the old
      name; **Colour** lists eight swatches with the section's own ticked. Clicking a header's
      chevron collapses its rows to the header; a right-click on a header offers that section's
      items. **Move section up** on End puts row 4 above rows 2–3, under its header. Relaunching
      with the same `--library=` shows the sections, colours and the collapsed one as left.
      **Remove section** puts its rows under the header above. Shuffle on shows no headers,
      stripes kept; off, the headers are back.

## Sounds — the user runs this

Against the running app and the rendered file. The falsifying material is a track with a
sharp percussive transient; the realism check is the authored boundary from the existing store.

- [ ] The outgoing track plays clean — no buzz, no silence, correct pitch, correct speed.
- [ ] Seeking lands where the bar was released, within the track — not at a tenth-of-track
      grid point.
- [ ] With the hit's track playing, capture at a drum hit; play the boundary; the fade **starts
      on the hit**, not after it.
- [ ] Seek back a few seconds and listen again; the point still lands on the hit.
- [ ] Mid-song, drag the coming fade-out's start mark on the seek bar a few seconds earlier: the
      transition begins where it was dropped.
- [ ] Tick **From end**, capture the fade-out point on a drum hit (it saves). Play through into
      the next track; the fade starts on the hit.
- [ ] During the crossfade, the time readout keeps following the outgoing track until it
      falls silent — it does not jump to the incoming track early.
- [ ] Seeking during the crossfade cancels the transition and the outgoing track continues
      alone.
- [ ] Render this boundary; open the file; the fade starts at the same place it did in the
      app.
- [ ] Mid-song, preview a draft: the song stops, the outgoing track plays from 5 s before its
      fade-out point, through the transition and 5 s on, then silence. Capture a better fade-in
      point while the incoming track plays and press **Preview again**: the new point is heard.
      The queue stays paused where it was; Play resumes it there.
- [ ] Preview on WASAPI exclusive plays: there is no second output to fail.
- [ ] In **Transitions…**, on a boundary whose open playlist overrides the fade-out point, **Preview**
      fades at the override's point and **Preview global** at the global's.
- [ ] Run the same capture-and-listen on WASAPI shared, WASAPI exclusive and WaveOut; the
      fade lands in the same place on all three.
- [ ] Delete the playing row; the next one starts cleanly from its beginning.
- [ ] Mid-song, drag a different track into the next place; it is the one that plays next.
      Dragging the playing row elsewhere does not interrupt it.
- [ ] The last track's tail plays to its very end before playback stops: no clipped final
      note, and no error.
- [ ] With repeat-all on, the last track passes into the first through their adjacency, as
      any other boundary does.
- [ ] Transition now mid-song fades into the next track as authored, starting from where you
      pressed.
- [ ] Rename a queued file before the track ahead of it starts; the queue plays on past it
      without a gap or a fade into silence.
- [ ] Unplug headphones mid-song; playback pauses and nothing plays from the speakers. Replug
      and press Play; it resumes where it paused.
- [ ] With *Crossfade for 8 s* on in **Settings…**, two tracks with no authored transition
      crossfade over 8 s. Switched on mid-track, the very next boundary crossfades.
- [ ] On a track with a ReplayGain tag (the library has two; tag one with rsgain or foobar2000
      if needed), toggling ReplayGain in **Settings…** mid-track changes the level at once,
      with no click. An untagged track does not change.
- [ ] Author an overlap of two loud masters, or play a boosted track: the clip light comes on
      as you hear the clip, not before, and names what you heard. Clear queue leaves it
      on; clicking it clears it.
- [ ] Two consecutive tracks of a continuous MP3 album (a live or segued one, whose files
      carry a LAME tag), with no transition authored, hard-cut with no audible gap.
- [ ] With another app holding the device exclusively, launch in exclusive mode; faderio falls
      back to shared and says so.
- [ ] Play a whole real album on WASAPI exclusive, through several queue advances and a track
      over 20 minutes long; it is clean throughout.
- [ ] Add the whole `MUSIC` library (**Add folder…** on the `MUSIC` folder) while a track plays;
      the track plays on without a dropout, and adding never starts or stops playback.
- [ ] Play a playlist whose files are on a network share, through several authored transitions
      and a FLAC over 20 minutes long; no transition is choppy as its fade begins, and the strip
      never says a track "was still being read" (ADR 0015).

## MusicBee — the plugin in the portable MusicBee

Run by every session that touches `src/Faderio.MusicBee`, and by the user for its Sounds half,
against the portable MusicBee in `musicbee_portable`
([phase 5](../.scratch/202609-faderio-phase-5-musicbee/spec.md), ADR 0012).

**Installing.** Build the plugin (`dotnet build src/Faderio.MusicBee`; beside a running faderio,
with `--artifacts-path`). Its `plugin\` output folder holds `mb_faderio.dll` and a `faderio\`
folder of everything it depends on. Close MusicBee, copy both into MusicBee's `Plugins\`, and
start it. A plugin that failed to load is recorded as disabled in MusicBee's
`AppData\MusicBee3Settings.ini` and stays so after it is fixed: enable it again in
**Preferences → Plugins**, or, with MusicBee closed, delete its `<State>` for
`mb_faderio.dll` so it is found as new.

**Its settings** are `AppData\faderio\settings.json`, in MusicBee's persistent storage folder
(the portable MusicBee's `AppData`), never standalone faderio's. faderio's dialog sets them:
**Edit → Edit Preferences → Plugins**, faderio's **Configure…**. What its OK chose is sent when
Preferences' **Save** or **Apply** is pressed. To start a run from a known state, write the file
by hand, with the library a standalone faderio library whose open playlist holds the tracks to
play:

```json
{ "schemaVersion": 2, "outputMode": "WasapiShared", "library": "C:\path\to\library" }
```

An `"outputDevice"` key names the device by its name in Windows (phase 5 issue 13); without it
the plugin plays on MusicBee's own device, or Windows' default when MusicBee's is DirectSound's
"Primary Sound Driver" or a device faderio cannot open.

The plugin's own `AppData\faderio\plugin.json` (phase 6 issue 08) holds what only the plugin
uses: `{ "nowPlayingTab": true }` puts faderio in the Now Playing tab. Configure…'s **Show
faderio in the Now Playing tab** sets it.

### Looks — the agent runs this

MusicBee draws its own window, so UI Automation sees neither its menus nor its transport.
Its main menu opens from a posted click on the "MusicBee ⌄" title, at (60, 19) in window
coordinates, with the window raised topmost. Walk it with posted arrow keys to the newest
dropdown (class `WindowsForms10.Window.20808…`): Tools is the fifth item, and faderio the last
item of Tools (Up from the first). Read each dropdown from a `PrintWindow` capture. A click
right after launch often does nothing: try again. The menu also closes between posted keys
while another app has the foreground, so a run beside the user's own work may not reach it.
Edit is the second item, and Edit Preferences the last of Edit. MusicBee shows faderio's dialog
controls to UI Automation as panes: find the folder box by its window class (`…EDIT…`) and set
it with `WM_SETTEXT`, the mode list with `CB_SETCURSEL`, and press buttons with a posted
`BM_CLICK`. Whether faderio is sounding is read from MusicBee's audio session meter
(`AudioMeterInformation.MasterPeakValue` of the session whose process is MusicBee's), which a
generated tone of amplitude 0.3 shows as 0.300.

**The panel** (phase 5 issue 10). faderio is a panel element: **View → Arrange Panels** lists
it under *available elements*, and ticking it puts it in the right sidebar; **Save** shows it
at once. MusicBee records it in `MusicBee3Settings.ini` as element `-61061107,0,0` in the
`LC_…` list of the panel it sits in, so with MusicBee closed it can be moved by hand, say from
`LC_RightSidebar` to `LC_RightMainPanel`. A drag in the dialog does not take posted mouse
messages. The view's controls are ordinary WinForms controls, so UI Automation sees them: find
the transport buttons by their window class (`…BUTTON…`) and their order on the row, not by
their glyph names, which UIA matches loosely (a lookup for ⏹ once returned the play button);
click them with a posted `BM_CLICK`. The two lists are `…SysListView32…`: the playlists above
the queue in a sidebar, beside it in a main panel. A playlist's rename needs keyboard focus,
which a background run cannot give.

- [ ] Installed as above, MusicBee starts with no error dialog, and after it is closed its
      `MusicBee3Settings.ini` lists `mb_faderio.dll` named "faderio", enabled, beside the
      plugins already there.
- [ ] **Tools** ends with **faderio**, whose submenu reads **Play**, **Stop**.
- [ ] With the settings file naming a library whose open playlist holds two generated tones,
      **Play** starts faderio: the meter shows the tone. **Stop** ends it: the meter reads 0.
- [ ] **The view** (phase 5 issue 10), placed as above and present after a restart: under a
      "faderio" header, the transport row ⏮ ▶ ⏹ ⏭ ⤳ 🔀 🔁, then −5 s, the seek bar, +5 s and
      "0:00 / -0:00"; **Playlists** listing the library's playlists with the open one bold on
      yellow; the queue with #, Title, Artist, Length and a ✎ on every row but the last, bold
      where this → next is authored (phase 5 issue 11), and row 1 marked at rest; the strip
      reading "Ready." and, at its right, "library · WASAPI shared at 48000 Hz on Speakers
      (…)", the device in use (phase 5 issue 13). In the
      sidebar as shipped (284 px) the buttons fit one row, with **Sync editor** at the right
      end of the playing track's line above them; in the sidebar it moves the editor only while
      the editor's page is open, never taking the queue away. The playlists sit above the
      queue, no column is off the edge, and the host line is cut short with its whole text as
      its tooltip.
- [ ] **▶** plays: the meter shows the tone, the track's name appears above the transport, the
      button reads ⏸, the readout and the thumb move, row 1 is marked. **⏸** pauses (meter 0,
      ▶ again); **▶** resumes; **⏹** stops: meter 0, the name blank, "0:00 / -0:00", the thumb
      at the start, row 1 at rest.
- [ ] **⏭** moves to row 2 from its start; **+5 s** and **−5 s** jump; a click in the middle
      of the seek bar lands at half the track; **⏮** past 3 s restarts the track.
- [ ] **Dragging the fades** (phase 6 issue 11). With row 1 playing into an authored row 2 and the
      editor open on it: the cursor is ↔ over a red mark above or below the groove and the move
      cursor over a grey wedge there. A mark dragged follows the mouse, and on release the strip
      says "Saved …" and the editor shows the new point and duration; a wedge dragged slides
      whole, the editor's duration unchanged.
- [ ] A double-click on a row plays that row from its start.
- [ ] A click on another playlist opens it: it goes bold, the queue shows its rows.
- [ ] A notice shows in the strip, not in a box: with faderio playing shared, an exclusive
      stream opened from outside (the probe below, with `Play()` before `Dispose()`) evicts it,
      and the strip reads "Audio output lost (Ex…" with a **1 notice** link; no message box
      appears. The link opens "faderio — notices", listing the time, the notice and the count,
      with the selected notice in full below. **▶** plays again after.
- [ ] **The device is free while faderio is stopped** (phase 5 issue 06). The probe below, run
      from a PowerShell 7 outside MusicBee, initialises an exclusive `WasapiOut` on the default
      device and closes it again. With the plugin loaded it reads "exclusive: opened and closed"
      before faderio has ever played, and again after **Play** then **Stop**; for each of the
      three output modes in the settings file, with a MusicBee restart between them. This is
      the check, not listening. Held by another process it reads "refused (…)": 0x8889000A,
      or "Not implemented" on the DragonFly, which answers that way while an exclusive stream
      is open, faderio's own included.
      Never run it while faderio plays: an exclusive stream evicts a shared one, so faderio
      would lose its device mid-play and say so, as it would under any exclusive-mode app.

      ```powershell
      $lib = "$env:USERPROFILE\.nuget\packages"
      Add-Type -Path "$lib\naudio.core\2.2.1\lib\netstandard2.0\NAudio.Core.dll"
      Add-Type -Path "$lib\naudio.wasapi\2.2.1\lib\netstandard2.0\NAudio.Wasapi.dll"
      $out = [NAudio.Wave.WasapiOut]::new([NAudio.CoreAudioApi.AudioClientShareMode]::Exclusive, 50)
      try { $out.Init([NAudio.Wave.SilenceProvider]::new([NAudio.Wave.WaveFormat]::new(44100, 16, 2))); "exclusive: opened and closed" }
      catch { "exclusive: refused ($($_.Exception.GetBaseException().Message))" }
      finally { $out.Dispose() }
      ```
- [ ] **The editor in a sidebar** (phase 5 issue 11). No editor shows until ✎ on row 1 is
      clicked: then "tone-a-440 → tone-b-660" opens as a page in the lists' place, under the
      transport and a **Close editor** bar, with the authored values in its fields, Editing:
      Global ticked, every field, From end, the curve, Preview, Remove transition and Render
      live, Undo and Redo beside Remove transition, scrolling inside where the panel is short
      (MusicBee stacks its sidebar panels); at 284 px Undo / Redo / Remove transition wrap onto
      two rows rather than being cut off at the edge. **Close editor** empties it and brings the
      lists back, a box being typed in saved first, with no question.
- [ ] **The editor in a main panel** (the element moved to `LC_RightMainPanel` as above): the
      editor is docked at the right from the start, 340 px wide, reading "No boundary
      selected" with its fields greyed, and no Close bar; ✎ on row 1 fills it.
- [ ] **A save and its notice.** Fade-out duration typed as "abc", Enter: the strip reads "Not
      saved: …" with a **1 notice** link and no box appears; the reason shows in red under the
      field. Typed as 3, Enter: "Saved tone-a-440 → tone-b-660." in the strip, ✎ on row 1 bold,
      and the library's `transitions.json` holds the 3 s fade-out duration, as standalone
      faderio would have saved it. **Undo** (and Ctrl+Z with focus in the editor) puts the
      duration back, "Undone: saved …"; **Redo** puts 3 back.
- [ ] **Preview** from the editor with faderio stopped: the meter shows sound, ▶ and the
      readout stay as they were ("0:00 / -0:00"), both Capture buttons come live, the button
      reads **Preview again**, **Stop preview** is live and the line under them says where the
      preview is ("Pre-roll · outgoing 0:05"). **Stop preview**: meter 0, the buttons as before.
      Then **▶** plays the queue from its start, and **⏹** stops it.
- [ ] **Capture reads its own track** (phase 6 issue 06). Row 1 playing, the editor on row 2's
      ✎: both Capture buttons greyed, a grey line under each point names its track ("Play *b*
      or preview to capture"). Playing row 2 brings the fade-out Capture live, its line gone and
      its tooltip "Capture *b*'s playhead"; the fade-in one stays greyed.
- [ ] **The traffic light** (phase 6 issue 07) at the editor's top right, beside the heading:
      red while row 1 plays with row 2's ✎ open, green while row 2 plays, amber while row 3
      plays, grey when stopped.
- [ ] **Leaving the editor with a box being typed in** (a duration typed, not yet left), by
      **Close editor** in a sidebar or a click on another playlist in a main panel, asks
      nothing: the duration is saved ("Saved …" in the strip) and the editor goes.
- [ ] **The transition manager** (phase 5 issue 12). The queue's right-click menu has
      **Transitions…** after the Edit transition items, live with any selection. With row 1
      selected it opens "faderio — transitions", a separate window owned by MusicBee's, on
      row 1's track: the picker names it, the legend reads "Each value reads global | override
      in *playlist*…", and the grid lists the authored crossfade as → *tone-b*, each value as
      "global | override" and the effective transition, with **Preview** and **Preview global**
      on the row. With no row selected it opens on the store's first track. **Preview** with
      faderio stopped: the meter shows sound, ▶ and the readout stay as they were, the line
      beside **Stop preview** reads "Pre-roll · outgoing 0:0…" counting up from 5 s before
      the fade-out point; **Stop preview** ends it. A
      double-click on the row opens it in the view's editor (the page in a sidebar), with
      MusicBee's window brought forward and the manager still open. A row whose file has been
      moved from under the store (the library's `transitions.json` naming a path that is not
      there, as auto-organise leaves one) reads "⚠ *path*", greyed, with "Not found where the
      store says: …" as its tooltip, and the picker marks it the same way; double-clicked, the
      editor says it is missing and can be removed, not changed, and **Remove transition**
      takes the row out of the manager. Esc and the close box hide the manager.
- [ ] **MusicBee's selection added** (phase 5 issue 14). A right-click on a track in
      MusicBee's main panel (a posted left click to select it, then a posted right click; the
      menu is a top-level `…Window.20808…` window, the item last in it) shows **Add to
      faderio's open playlist** at the end of MusicBee's context menu. On a promised track it
      appends that track to the view's queue and to the open playlist's `.m3u8`, with no
      notice; on a track MusicBee lists but whose file is gone (an album on a
      drive that is not there) the strip reads "Nothing added: the file was rejected — …:
      The file does not exist. …" with a **1 notice** link and no box. Neither player starts
      or stops: added while MusicBee's own player plays, MusicBee keeps playing and faderio's
      ▶ and "0:00 / -0:00" stay as they were. Restore the playlist file after.
- [ ] **Tracks dropped on the view** (phase 6 issues 01 and 03). Tracks dragged from MusicBee's
      main panel onto the view's queue show the copy cursor and the drop line, and land on it
      (below the rows, at the end) in the queue and the open playlist's `.m3u8`, in the drag's
      order, with no notice, the view docked and floating alike; such a gone track dropped gives the strip's rejection with
      **1 notice**, as the context item does. A drag of text is refused (no-entry cursor).
      Neither player starts or stops. A shell drag needs a real mouse, which posted messages
      cannot start: the user drags, the session reads the queue and the file. Restore the
      playlist file after.
- [ ] **Rows dragged in the view** (phase 6 issue 02). Selected rows dragged in the view's
      queue show the drop line and land on it, still selected, docked and floating alike;
      playback goes on. The user drags (a real mouse); the session reads the playlist file.
- [ ] **One holder at a time** (phase 5 issue 15). With standalone faderio open on the smoke
      library, MusicBee starts with a box owned by it reading "The library library could not be
      opened (it is open in faderio). Nothing is saved until a library opens; what is queued
      still plays.", the strip the same with **1 notice**, the host line "library (could not be
      opened) · …", and an empty queue; `.faderio\host.lock` in the library reads "faderio".
      faderio closed, then Configure… and Preferences' **Save** on the same folder: the
      playlists and queue fill, no new notice, the file reads "MusicBee". The reverse, standalone
      faderio launched on the library MusicBee holds, reads "… (it is open in MusicBee) …" in
      its strip; MusicBee closed and Open library… on the folder opens it.
- [ ] **Edit → Edit Preferences → Plugins** lists faderio with a **Configure…** button, which
      opens faderio's dialog, owned by MusicBee's main window. It shows the settings file's
      library folder, its output mode by name ("WASAPI shared") and its output device (phase 5
      issue 13): "MusicBee's device" first, with MusicBee's device named in brackets when it is
      one faderio can open, then every active render device by name; with "The mode applies
      when MusicBee next starts; the device when faderio next plays from stopped."
- [ ] **The output device** (phase 5 issue 13). Another device chosen (a second render device,
      say the Steam virtual speakers) and OK: the file is unchanged, and Configure… again opens
      on it. Preferences' **Save** writes `"outputDevice"` to the file and the strip reads
      "Saved machine settings." with no box; the host line still names the device in use. **▶**
      plays on the new device: its session meter shows the tone and the old device's reads 0,
      and the host line reads "… on *the new device*". **⏹** stops it.
- [ ] **A device that is not there.** With `"outputDevice": "Gone DAC"` written by hand and
      MusicBee restarted, a box owned by MusicBee reads "Output device "Gone DAC" was not found;
      using the default device." (the view is not yet attached at start), the strip the same
      with **1 notice**, and the host line names the default device. Configure… lists "Gone
      DAC (not found)", selected; Cancel keeps it in the file. **▶** plays on the default device
      and repeats the notice (the device is tried again at each open); **⏹** stops it.
- [ ] **No notice at start in exclusive** (phase 5 issue 13). With `"outputMode":
      "WasapiExclusive"` and MusicBee restarted: no box, the strip reads "Ready." and the host
      line "library · WASAPI exclusive at 48000 Hz on …". The exclusive probe below opens
      before any play, is refused while **▶** plays, and opens again after **⏹**.
- [ ] Choosing another library folder and **WaveOut**, then OK and Preferences' **Save**,
      writes both to the settings file and shows "Saved machine settings.". After MusicBee is
      restarted, the dialog opens on them. Cancel, or closing Preferences unsaved, changes
      nothing.
- [ ] A folder that does not exist, typed and saved, shows "The library … could not be opened
      (the folder does not exist)…" in a box owned by MusicBee, and the settings file keeps the
      library before it. A relative path is refused by the dialog itself, which stays open.
- [ ] **faderio in the Now Playing tab** (phase 6 issue 08, ADR 0013). Configure… shows **Show
      faderio in the Now Playing tab** under the device, with "In place of MusicBee's own view,
      from the next visit to the tab."; ticked, OK and Preferences' **Save** write
      `"nowPlayingTab": true` to `plugin.json`. With no restart, the Now Playing tab then shows
      faderio's view filling the centre and the right sidebar's place (playlists beside the
      queue, the editor at the right, the transport above), none of MusicBee's Now Playing
      content showing through, and no box. Restoring and maximising the window resizes it with
      the window. The library tabs (MUSIC, INBOX…) look as before, the sidebar's faderio panel
      included. A transition opened with ✎ there is open again after a library tab and back.
      Three round trips leave no notice and no blank tab. Unticked and saved, the next visit
      shows MusicBee's own Now Playing.
- [ ] **Three panes, three titles** (phase 6). In a main panel or the Now Playing tab, "Playlists",
      "Queue" and "Transition editor" sit on one line, and the three boxes under them start and
      end level. In the sidebar "Queue" sits over the queue as "Playlists" does over its list,
      and the editor's page has its Close bar, no title.
- [ ] **Sections** (phase 6 issue 09). In the sidebar, the queue menu's **Section → Start a
      section here…** on a row shows a header with its swatch, name and chevron, its rows striped in
      its colour, as in standalone faderio; the chevron collapses it, and MusicBee restarted shows
      it collapsed still.
- [ ] With `Plugins\faderio\` removed, MusicBee starts, and a message box owned by it reads
      "faderio could not start: Could not load file or assembly 'Faderio.Core…'". Put the
      folder back.

### Sounds — the user runs this

- [ ] With MusicBee playing a track, **Tools → faderio → Play** stops it and plays the open
      playlist: the two players never sound at once. So do the view's **▶** and a row's
      double-click (phase 5 issue 10).
- [ ] An authored crossfade in the open playlist is heard as in standalone faderio.
- [ ] **Preview** in the plugin's editor (phase 5 issue 11) is heard as standalone faderio's
      preview of the same draft: the pre-roll, the crossfade, the tail, then silence with the
      queue where it was. With MusicBee playing a track, Preview stops it first.
- [ ] A transition authored in MusicBee's editor plays the same in standalone faderio opened
      on that library, and one authored in standalone faderio plays the same in MusicBee.
- [ ] After faderio's **Stop**, MusicBee's own player plays again straight away on WASAPI
      shared, and within a few seconds on WASAPI exclusive (the output mode in faderio's
      Configure… dialog, then a MusicBee restart). In exclusive, MusicBee's "unable to start
      playback" on a start made at once after Stop is the known limit accepted at the spike's
      verdict (ADR 0012); a second try plays.
- [ ] Playback survives MusicBee's other plugins being enabled.
- [ ] **▶ while MusicBee's own player plays, in WASAPI exclusive** (phase 5 issue 13): faderio
      stops MusicBee's player and plays, shared, with "WASAPI exclusive unavailable (Not
      implemented); trying WASAPI shared." in the strip: the known limit (ADR 0012). Once
      MusicBee's own player has played, its stream refuses an exclusive open from the same
      process for the rest of the session. Before it has played, ▶ opens exclusive.
- [ ] **A device chosen in Configure…** is heard on that device at the next **▶** from stopped,
      with no restart.
