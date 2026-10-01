# 14: Adding MusicBee's selection to the open playlist

Status: resolved
Blocked by: 05, 10

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 9, ADR
0012). Staged at the verdict; specified below (2026-10-01) before it was built.

## What to build

A context-menu item in MusicBee that adds the selected tracks
(`Library_QueryFilesEx("domain=SelectedFiles")`) to faderio's open playlist. Every file is
probed, and rejections are listed together as **Add folder…** lists them. Drag-and-drop is out
of scope.

## Implementation decisions

- **The item.** One item, **Add to faderio's open playlist**, on the main panel's track
  context menu (`context.Main/…`), registered at `Initialise` beside the Tools menu, where the
  smoke has shown MusicBee's menu items take. It is the only context item: the view's own
  queue menu is faderio's, this one is MusicBee's.
- **The selection.** The handler asks MusicBee for its selection,
  `Library_QueryFilesEx("domain=SelectedFiles")`, and sends the files it answers, in MusicBee's
  order, to the engine's `Add` in one call: the same call standalone faderio's **Add files…**
  and **Add folder…** make. MusicBee answering false, null or nothing is nothing to add: no
  engine call and no notice, as the App's picker cancelled. Entries that are not files on
  disk (a stream's URL, a CD track) go to the engine as they are, which probes and rejects
  each with its reason, so nothing is filtered in the shell.
- **Probing and rejections** are the engine's, unchanged: each file is probed and its tags
  read off the UI thread, the rows appear as they resolve with "Adding *n* of *m*…" in the
  view's strip, and when the run ends one notice names every rejection with its reason,
  exactly as **Add folder…** reports them (ADR 0007). The notice takes the plugin's route:
  the strip of every attached view, or a box owned by MusicBee's window with none.
- **Playback is left alone.** Adding never starts faderio's playback and never stops
  MusicBee's player: the rows are the news. The open playlist is saved as the add goes, as it
  is in standalone faderio, so the rows are there at the next start in either host.
- **No engine.** With the engine not started (the dependencies missing, say), the item
  repeats the start failure, as the Tools items do.
- **Placement.** One user intent, one engine call (`Add`); the shell neither lists, probes
  nor filters. The selection is MusicBee's, read and sent, never held.

## Testing decisions

Seam 2, the plugin through its own entry point: `FakeMusicBee` gains `Library_QueryFilesEx`,
answering a selection the test sets and recording the query, and the context item is clicked
as the Tools items are.

- `Initialise` registers the context item (the registration test's call list grows by one).
- Two tones selected, with the open playlist holding two: the item sends one query,
  `domain=SelectedFiles`; the queue reads the four in order once the add ends; the playlist
  file holds them; the output never played; no notice.
- A selection of a tone, a text file named `.wav` and a path that is not there: the tone is
  added, and one notice reads "Added 1 track; rejected 2 — …" naming each rejected file with
  its reason, as `AddReport.Summarise` writes it.
- MusicBee answering nothing, or false: no row, no notice, and the query made.
- No test asks for the failure box with the engine not started: the item goes through the
  same `Ready` as the Tools items, which issue 03 covered.

The MusicBee **Looks** half gains an item: the context menu on a track in MusicBee's main
panel ends with **Add to faderio's open playlist**; chosen on a promised track it appends the
track to the view's queue and the playlist file with no notice, and on a track MusicBee lists
but whose file is gone (the portable MusicBee's one such album; every other file in it is
promised) the strip shows the rejection with its reason and a **1 notice** link. Added while
MusicBee's own player plays, neither player starts or stops.

## Acceptance criteria

- [x] The selection is added to the open playlist in one engine call.
- [x] Rejected files are listed together, each naming what disqualifies it.
- [x] Headless tests drive it through the plugin's entry point with a fake MusicBee.

## Comments

- **Resolved 2026-10-01.** Built as specified above.
  - **The plugin:** `Initialise` registers `context.Main/Add to faderio's open playlist`
    beside the Tools menu; its handler asks `Library_QueryFilesEx("domain=SelectedFiles")`
    and sends the files to `Host.Add`, one `Engine.Add`. Nothing else changed: the engine's
    probing, the rows as they resolve, the run's one notice and the playlist save are as
    standalone faderio's Add folder…. Registering at `Initialise` works: the forum advice to
    add context items at the PluginStartup notification was not needed on 3.6.9403.
  - **Tests:** `FakeMusicBee` answers the selection query and records it; `PluginTests`'s
    registration test lists the new item; `PluginTests.Selection`: two tones appended in
    MusicBee's order, one query, no `Player_Stop`, the playlist file holding four, the output
    never played, no notice; a tone, a text file named `.wav` and a missing path giving one
    notice "Added 1 track; rejected 2 — notes.wav: …; missing.flac: The file does not
    exist."; an empty or refused selection adding nothing, silently. The poll helpers
    (`Eventually`, `Added`, `SavedOpenPlaylist`) wait for the background add and for the
    playlist's write, which follows the publication and can be mid-replace when read.
  - **Found on the way:** issue 13's `ReleasingTheDeviceTests` waited for the fake player's
    state to turn Stopped and then asserted its stop had been posted, but the post follows
    the state change, so under the full suite's load one of them failed once. The four sites
    now wait for the post itself (`Posted`).
  - **The MusicBee Looks half** on the portable MusicBee (DirectSound, shared, the view in
    the right sidebar), the plugin smoked before the review's edits, which changed a doc
    comment and a parameter type only:
    - ✅ Loads with no box; the strip "Ready."; Tools → faderio reads Play, Stop; Play from
      there sounds (0.300) and ⏹ ends it.
    - ✅ A right-click on a library track shows MusicBee's menu ending with **Add to faderio's
      open playlist**; chosen, that track (4:46) is row 3 of the queue and the
      third line of `Untitled.m3u8`, the strip "Ready.", meter 0, no box.
    - ✅ On a track whose file is on a drive that is not there: the strip "Nothing
      added: the file was rejected — 100 - ….mp3: The file does not exist.
      See docs/supported-audio.md." with **1 notice**, no box, the queue unchanged.
    - ✅ With MusicBee's own player playing, a library track added: MusicBee kept playing (0:09 /
      -4:37), the track became row 4 and the fourth line of the file, faderio's ▶ and
      "0:00 / -0:00" unchanged.
    - The smoke library's playlist file restored after. The "a promised file and an
      unsupported one in one selection" case of the first draft is the headless test's: the
      portable MusicBee lists no unsupported file, and a selection across albums is beyond
      posted clicks.
  - **Review (2026-10-01).** Standards: no hard breach. Taken: the duplicate registration
    test dropped, `Eventually` beside `Launched` and `Launched` written over it, `Added`
    taking the row count, `missing` for what CONTEXT.md's **Gone** does not mean,
    `Host.Add` taking an `IEnumerable`, the handler's comment trimmed. Left: `Host.Add` as a
    one-line delegation, the house style of `Play` and `Stop`. Spec: the two-tone test now
    counts the query and asserts no `Player_Stop`; the Looks sentence matches the smoke
    item; the engine-not-started path stays untested here (issue 03's `Ready`).
