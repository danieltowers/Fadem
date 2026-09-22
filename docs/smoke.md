# Smoke checklist

Run by **every session that touches the shell**, before that session's commit. Decided in
[wayfinder ticket 06](../.scratch/202609-faderio-v3/issues/06-how-do-we-make-green-tests-and-a-broken-app-impossible.md).

The list is in two halves, each run by the party that can actually judge it. A session never
reports "smoke passed" on items it could not perceive.

Scoped to the slice as defined in
[ticket 05](../.scratch/202609-faderio-v3/issues/05-what-exactly-is-in-the-narrow-slice.md).
Extend it when the shell grows; never let an item go stale.

## Looks — the agent runs this

Launch the app (see the `run` skill), take a screenshot, and check each item against it.
Report the result item by item in the session's hand-off.

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
- [ ] Reaching the end of the second track stops playback cleanly with no error.
