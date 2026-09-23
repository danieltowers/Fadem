# What does queue management add?

Type: grilling
Status: open
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
