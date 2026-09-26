# Decisions — interim improvements

Grilled from [brief.md](brief.md) on 2026-09-26. The three specs cite these by number; **when a
spec and this file disagree, this file wins.** Glossary terms settled here are already in
`CONTEXT.md` (Library, Playlist, Queue, Attended row).

## Shape

- **D1** Three specs, built in this order: [interim-transport](../202609-faderio-interim-transport/spec.md)
  (no storage) → [library](../202609-faderio-library/spec.md) (where everything lives) →
  [playlists](../202609-faderio-playlists/spec.md) (on top of the library).
- **D2** [ADR 0011](../../docs/adr/0011-the-library-folder-owns-the-store.md) records the library move.

## Transport

- **D3** The ⏹ button is hidden. `Stop()` stays in the engine, untouched. No resting-playhead
  work: at launch and after the queue ends the seek bar is disabled and Play starts the attended
  row from 0:00, as today.
- **D4** Play with a row selected that is not the attended row plays that row from its start;
  otherwise Play resumes. Applies paused, at launch and after the queue ends. Double-click plays
  a row regardless. *Refined while building transport 02:* while stopped, a selected row always
  plays, even the marked one; "resume" applies only while paused.
- **D5** The attended-row marker shows in every transport state, dimmed when nothing sounds. It
  means "what Play would do", never "sounding now". *Decided while building transport 03,
  replacing Q24's "last row played":* when the queue ends the mark returns to row 0 and ▶ plays
  it.
- **D6** The seek bar becomes a custom bar: a click anywhere jumps there; a drag seeks on
  release, not live.
- **D7** ±5 s buttons, fixed, both directions, on the attended voice. The engine gets a
  relative-seek command; a jump may land inside a transition and is treated as any seek.
- **D8** ±0.1 s nudge buttons on the two **points** only, never on durations. Shift-click is
  1 s. They act on the displayed number, so under From end `+` moves the point earlier in the
  track. No automatic preview.
- **D9** A button at the far right of row N opens N→N+1 in the editor panel. The last row has
  none. The panel keeps whatever Global / This playlist mode it was last in (D23).

## Library

- **D10** **Library** = a folder the user chooses = one faderio instance. Audio may live
  anywhere. faderio writes only to `<library>/.faderio/`: `transitions.json`,
  `playlists/*.m3u8`, `settings.json`.
- **D11** Library `settings.json` holds the listening settings: `UnauthoredAdjacency`,
  `DefaultCrossfadeSeconds`, `ReplayGainEnabled`, plus shuffle and repeat. AppData keeps
  `OutputMode`, the last-opened library and window geometry.
- **D12** First launch with no remembered library prompts for a folder, defaulting to the
  user's Music folder. **Open library…** switches in place: stop, clear, reload. No restart.
- **D13** No migration of today's AppData store or settings; a new library starts empty.

## Playlists

- **D14** A playlist is a `.m3u8` in `<library>/.faderio/playlists/`; its name is its filename.
  Rename renames the file and rekeys its overrides. External edits of that folder are the
  user's problem.
- **D15** Exactly one playlist is open at all times; the queue mirrors it and every queue edit
  autosaves. There is no scratch queue. Adding with none open creates "Untitled". An Untitled
  with no rows is deleted on switch or quit; non-empty ones number "Untitled 2", "Untitled 3"…
- **D16** Left sidebar lists the library's playlists. Click opens (replaces the queue); the
  sounding voice finishes first, and the join old-track → new row 0 is an adjacency resolved
  from the store like any other. Right-click: New / Rename (inline) / Delete. No drag-to-add.
- **D17** Delete removes the file and its overrides. If it was open, the first remaining
  playlist opens, or Untitled if none; playback is not interrupted (D16 join rule).
- **D18** The open playlist's name shows above the queue (double-click or F2 renames inline)
  and in the title bar as "*name* — faderio".
- **D19** "Open playlist…" becomes **Import playlist…** (copies the file into the library and
  opens it). "Save as playlist…" becomes **Export playlist…** (writes to any path). Add files /
  Add folder append to the open playlist.
- **D20** At launch the last open playlist opens with row 0 attended at 0:00.
- **D21** The engine passes the open playlist's override to the resolver. The editor panel gets
  "Editing: ● Global ○ This playlist"; in playlist mode each field shows the global value as a
  greyed placeholder and only what is typed becomes the override; "Remove override" sits
  beside "Remove transition".
- **D22** An override with no global transition is allowed (a playlist-only transition). The
  manager flags it "playlist-only".
- **D23** The row button (D9) opens the panel in its last mode.
- **D24** Transition manager: its own modeless window. Opens on the queue's selected row's
  track, with a picker over every track in the store. Lists every adjacency the track is in,
  global and open-playlist override side by side. Preview plays the effective transition for
  the open playlist; a second button previews the global one. Read-only otherwise: editing
  goes through the panel.
- **D25** Columns: #, Title, Artist, Album, Length, Sample rate/bit depth, Replay gain, Path.
  Album is read from tags; the probe's format is kept on the Track. Right-click on the header
  toggles columns; drag reorders; widths persist. Persisted per library. Display only: no
  album gain, no replay-gain playback change.
