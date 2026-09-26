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
fallback chain. The output mode is a setting, chosen in **Settings…** and used from the next
launch:

```
dotnet run --project src/Faderio.App -- "<outgoing>" "<incoming>" --play --edit-first
```

`<outgoing>` and `<incoming>` are **absolute paths to two real, promised files**; there are no
sample files in the repo. `dotnet run` runs the app from `src/Faderio.App`, so a relative path
resolves there, not where you typed the command. A path that does not exist is rejected like
any other file: nothing is queued and the only sign is one line in the status strip
("Nothing added: … The file does not exist."). The agent generates its own WAVs for the Looks
half; the Sounds half uses the two tracks of a boundary already in the store
(`%APPDATA%\faderio\transitions.json` lists them by full path).

Capture the window with `PrintWindow` rather than a screen grab, so it need not be in front.
There is no ⏹ button (the engine still stops at launch and when the queue ends). UI Automation
folds some symbol glyphs together, so find a transport button by comparing names ordinally, not
with a name condition. The status strip's items are not exposed to it; read them
from a capture. The seek bar is a pane named "Seek" whose thumb is not an element of its own: find
the thumb in a capture (the highlight colour), and click it with posted mouse messages.

- [ ] The window opens without an error dialog.
- [ ] Add-files puts both tracks in the list, each with a title (or filename) and no blank row.
- [ ] The transport row reads ⏮ ▶ ⏭ ⤳ 🔀 🔁, then −5 s, the seek bar, +5 s and the time; no ⏹.
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
- [ ] **+5 s** twice moves the readout on ten seconds and **−5 s** back five, playing or paused;
      near the start −5 s stops at 0:00. Both are greyed out at launch, with the seek bar.
- [ ] The time readout shows elapsed and remaining, and remaining never reads `0:00` mid-track.
- [ ] Right-clicking a row shows "Edit transition: this → next" and "Edit transition: previous
      → this", and, only while repeat is on one, "Edit transition: this → this". All read
      clearly, and a disabled one looks disabled. With repeat off, this → next is disabled on
      the last row and previous → this on the first. Under repeat-all, this → next on the last
      row opens last → first. This → this opens the row into itself.
- [ ] With unsaved edits (a field, **From end** or the curve changed, or **Remove transition**
      pressed), opening another adjacency asks **Save / Discard / Cancel**, and so does closing
      the window. Cancel keeps the edits and stays; Discard drops them; Save saves and moves on,
      unless the save is refused. With nothing unsaved it asks nothing.
- [ ] The transition editor opens with its title fully visible — not clipped, not behind
      another control.
- [ ] All four parameter fields, the capture buttons, **From end** beside the fade-out point
      and the **Fade curve** list are visible without scrolling at the default window size.
- [ ] Save, Revert, Remove transition and Render are visible and enabled once the editor is
      open on an adjacency.
- [ ] Ticking **From end** on a fade-out point of 3.5 on a 5 s track shows 1.5; unticking shows
      3.5 again. The fade curve lists Equal-power and Linear.
- [ ] Saving a fade-out point past the end of its track stores nothing: the strip says "Not
      saved", and the reason shows in red under the field, readable in full, without pushing
      the buttons out of sight. The next good save clears it.
- [ ] **Remove transition** says "Removed …" in the strip and leaves the fields filled; Save
      puts the transition back. **Revert** reloads what is stored.
- [ ] Pressing capture writes a number into the field, and it is the number the time readout
      showed at that moment.
- [ ] After Save, the status strip shows a confirmation and nothing else changed.
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
      dropped, each folder expanded in place.
- [ ] **Save as playlist…** then **Open playlist…** on the file just saved doubles the queue
      in the same order, and the status strip names the file saved. Save it in a folder whose
      name has `†` or `⭐️`.
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
- [ ] The status strip shows the latest notice and, at its right, a count ("3 notices").
      Pressing Save on the same boundary several times shows one "Saved … ×n", not n notices.
      A notice longer than the strip is clipped and never pushes the count off. Clicking the
      notice or the count opens the notice list: every notice this session, oldest first, with
      its time and its count, and the selected one in full below. Esc and the close box hide
      it, and clicking the strip again brings back the same list.
- [ ] At the strip's far right, the clip light reads "● No clips", dark and greyed, by default.
      Lit, it reads "● 2 clips · latest: A → B" in white on red, legible at a glance, and a
      long cause is cut short without pushing the notice count off; its tooltip has it whole.
      Clicking it turns it dark again. (A track tagged well above 0 dB, with ReplayGain on,
      lights it.)
- [ ] Launching with a corrupt `%APPDATA%\faderio\transitions.json` (back it up first) shows
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
- [ ] **Settings…** opens a dialog with OK and Cancel showing the three settings: the output
      mode with "Applies at the next launch." under it; *Hard cut* or *Crossfade for N s*, with
      N greyed out under *Hard cut*; and ReplayGain. Launched into a fallback as above, it also
      reads "In use now: WASAPI shared" (or WaveOut) under the mode chosen.
- [ ] A setting changed and saved with OK is still set after a restart; Cancel keeps nothing.
- [ ] Launching with a corrupt `%APPDATA%\faderio\settings.json` (back it up first) shows no
      dialog: the strip says the file was moved aside to `settings.json.corrupt`, and
      **Settings…** shows the defaults.
- [ ] Adding the library's longest FLAC (about 26 minutes) shows its full length in the time
      readout, and seeking into its last minute lands there. Length is not proven by the test
      suite; this is where it is checked.

## Sounds — the user runs this

Against the running app and the rendered file. The falsifying material is a track with a
sharp percussive transient; the realism check is the authored boundary from the existing store.

- [ ] The outgoing track plays clean — no buzz, no silence, correct pitch, correct speed.
- [ ] Seeking lands where the bar was released, within the track — not at a tenth-of-track
      grid point.
- [ ] Capture at a drum hit; play the boundary; the fade **starts on the hit**, not after it.
- [ ] Seek back a few seconds and listen again; the point still lands on the hit.
- [ ] Tick **From end**, capture the fade-out point on a drum hit and save. Play through into
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
