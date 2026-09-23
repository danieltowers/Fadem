# What does queue management add?

Type: grilling
Status: resolved
Blocked by: 01

## Question

The map's Notes put queue management first for the daily driver, but no ticket decides it.
The audit in [Audit the engine against v2's intent](01-audit-the-engine-against-v2s-intent.md)
found that the engine has `Remove`, `Clear`, `SetShuffle` and `SetRepeat` with no shell over
them, and has no folder add, no reorder and no M3U command. Decide what the daily driver's
queue does, with one engine call per user intent (placement test 1):

- **Add folder.** What it enumerates: recursive or not, which extensions, and in what order.
  Whether rejected files are summarised the way `AddReport` summarises them today. Whether the
  probe runs in the background belongs to
  [Does adding a folder need a background probe?](07-does-adding-a-folder-need-a-background-probe.md).
- **Reorder.** Whether it is wanted at all, since v2 never had it. If so, the gesture
  (drag, or move up and down) and the engine member. Also what moving the playing row, or the
  row after it, does to a pending boundary.
- **Remove and clear.** The gestures (Delete key, context menu, a button). What removing the
  playing row does: today it restarts at the same index. Neither command has a test at any seam.
- **M3U.** Save and load as engine commands, not a shell that reads `View.Queue`. Whether load
  replaces or appends to the queue. Whether relative paths resolve against the playlist's
  folder (today they resolve against the process working directory). Whether save writes
  absolute or relative paths, and whether `#EXTINF` keeps rounding `Length` to whole seconds.
- **Shuffle and repeat controls.** Where they sit and how they show `View.Shuffle` and
  `View.Repeat`. What they do is decided in
  [What do skip, repeat and end-of-queue do?](02-what-do-skip-repeat-and-end-of-queue-do.md).

## Comments

- From [Does adding a folder need a background probe?](07-does-adding-a-folder-need-a-background-probe.md):
  yes. The whole library takes 12.8 s cold, and today's `AddTracks` probes under the engine lock
  that the audio thread also takes, so any add during playback stalls the audio. Adding (files,
  folder, M3U) must probe off the lock and off the UI thread, with rows appended as files
  resolve. Decide the command surface for that here: progress, cancelling a long add, and where
  the rows land.

## Answer

Grilled with the user on 2026-09-23, starting from the audit and from
[Does adding a folder need a background probe?](07-does-adding-a-folder-need-a-background-probe.md).
`CONTEXT.md` gains **Row**, and **Queue** is sharpened: saving the queue writes a new playlist;
the queue itself is still never saved.

### Adding: one call, in the background

- **`Add(paths)`** takes any mix of files, folders and `.m3u`/`.m3u8` playlists. **Add files…**,
  a new **Add folder…**, and dragging from Explorer onto the queue all call it. "Put these in
  my queue" is one intent (placement test 1).
- **Folders** are enumerated recursively, and only `.wav`, `.flac` and `.mp3` are taken.
  Everything else (images, logs, video) is ignored silently. Files are ordered by path, folder
  by folder, with **natural sort** ("2 - …" before "10 - …"). A file with a promised extension
  that fails the probe is **rejected**, as today (ADR 0007).
- **Background.** The call returns at once. Probing and tag reading run off the engine lock and
  off the UI thread. Rows are **appended at the end, in the given order**, as files resolve.
  `PlayerView` shows progress (*Adding 120 of 903*). **Cancel** is one call and keeps the rows
  already added. A second add waits behind the first. On completion, the rejections arrive as
  one notice.
- **Adding never starts playback**, not even into an empty, stopped queue. `--play` stays the
  smoke-run shortcut.
- **Duplicates are allowed** as separate rows. Two copies side by side form an A→A adjacency
  and share its transition, the same one repeat-one fires.

### Reorder

Drag rows within the queue, with multi-select, as one call: **`Move(rows, before)`**.

- Rows keep their identity, so moving the playing row does not interrupt it.
- If a move changes which row comes next, the pending boundary is rescheduled, as long as its
  transition has not started. A transition in flight plays out (the audible-time rule).
- Under shuffle, moving changes the shuffled order, which is the visible order.

### Remove and clear

- **Remove:** the Delete key or the context menu's **Remove**, on the selected rows, as one
  call: **`Remove(rows)`**. Today's `Remove(row)` becomes its one-row case.
- **Removing the playing row** keeps today's behaviour. Playback continues on the row that
  takes its place, from 0:00, as a hard cut. Paused stays paused, and removing the last row
  finishes playback. In a transition, a removed voice is cut, and the other plays on at full
  volume, as a skip lands. Removing what plays means "not this one", not "stop". This is
  untested today, and the build adds tests.
- **Correction to [What is preview?](04-what-is-preview.md):** a held queue whose playing row
  is removed lands **paused on the next row**, not *Stopped*.
- **Clear queue** in the context menu asks for confirmation when the queue has more than one
  row, because there is no undo.

### M3U

- **Save:** **`SaveAsPlaylist(path)`**, an engine command, writes the **visible order** (under
  shuffle, the play order) as **UTF-8 `.m3u8`** with **absolute paths** and `#EXTINF` in whole
  seconds. UTF-8 is required: library folder names contain `†` and `⭐️`. Paths are absolute
  because the transition store is keyed by full path. The static `M3UPlaylist` helper moves
  behind the engine.
- **Load** goes through `Add`, so it **appends**. Relative paths resolve against the
  **playlist's folder**, not the working directory. A missing file is a rejection with its
  reason. A `.m3u` that is not valid UTF-8 is read in the system code page.
- **There is no "replace the queue".** Clear, then Add: two intents the user expresses.

### Shuffle and repeat controls

A **shuffle toggle** and a **repeat button** cycling *off → all → one* sit beside the transport
buttons. Each shows its state from `View.Shuffle` and `View.Repeat`, and each press is one
engine call. What they do is settled in
[What do skip, repeat and end-of-queue do?](02-what-do-skip-repeat-and-end-of-queue-do.md).
