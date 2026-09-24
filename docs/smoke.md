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
`--edit-first` opens the editor on the first boundary; `--output=` picks the mode:

```
dotnet run --project src/Faderio.App -- "<outgoing>" "<incoming>" --play --edit-first --output=wasapi-shared
```

`<outgoing>` and `<incoming>` are **absolute paths to two real, promised files**; there are no
sample files in the repo. `dotnet run` runs the app from `src/Faderio.App`, so a relative path
resolves there, not where you typed the command. A path that does not exist is rejected like
any other file: nothing is queued and the only sign is one line in the status strip
("Nothing added: … The file does not exist."). The agent generates its own WAVs for the Looks
half; the Sounds half uses the two tracks of a boundary already in the store
(`%APPDATA%\faderio\transitions.json` lists them by full path).

Capture the window with `PrintWindow` rather than a screen grab, so it need not be in front.

- [ ] The window opens without an error dialog.
- [ ] Add-files puts both tracks in the list, each with a title (or filename) and no blank row.
- [ ] The seek bar has visible travel across the width of the transport area, not a stub.
- [ ] While playing, the seek thumb moves smoothly — not in visible steps.
- [ ] The time readout shows elapsed and remaining, and remaining never reads `0:00` mid-track.
- [ ] Right-clicking a row shows "Edit transition (this → next)", enabled on the first row and
      disabled on the last.
- [ ] The transition editor opens with its title fully visible — not clipped, not behind
      another control.
- [ ] All four parameter fields and the capture buttons are visible without scrolling at the
      default window size.
- [ ] The Save and Render buttons are visible and enabled once a boundary is selected.
- [ ] Pressing capture writes a number into the field, and it is the number the time readout
      showed at that moment.
- [ ] After Save, the status strip shows a confirmation and nothing else changed.
- [ ] Resizing the window narrower does not hide the transport or the editor's buttons.
- [ ] While adding a folder's worth of files (fifty or more, through **Add files…**, **Add
      folder…** or as arguments), the window stays responsive — it moves and repaints — and
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
- [ ] While playing on WASAPI shared, pressing ⏹, **Delete** on the only row and **Clear
      queue** each stop playback, and the window keeps rendering afterwards: the play button
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
- [ ] Launching with a corrupt `%APPDATA%\faderio\transitions.json` (back it up first) shows
      no dialog: the strip says the file was moved aside to `transitions.json.corrupt`.
- [ ] A queued file renamed before the track ahead of it starts is marked when that track starts:
      "⚠" before its row number, the row greyed, and a tooltip saying it is unreadable. The
      strip says it was passed over, once, and the queue plays on past it. (The file is opened
      as the track before it starts, so renaming it later still plays it.)
- [ ] Adding a rejected file (an `.m4a`, an `.ogg`, or a file with more than two channels)
      leaves the list unchanged and shows one notice naming the property that failed and
      pointing at `docs/supported-audio.md`. Nothing is queued, nothing plays.
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
- [ ] During the crossfade, the time readout keeps following the outgoing track until it
      falls silent — it does not jump to the incoming track early.
- [ ] Seeking during the crossfade cancels the transition and the outgoing track continues
      alone.
- [ ] Render this boundary; open the file; the fade starts at the same place it did in the
      app.
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
- [ ] Add the whole `MUSIC` library (**Add folder…** on the `MUSIC` folder) while a track plays;
      the track plays on without a dropout, and adding never starts or stops playback.
