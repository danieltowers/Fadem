# 01: Adding runs in the background

Status: ready-for-agent
Blocked by: —

Decided in [What does queue management add?](../../202609-faderio-phase-1-restage/issues/12-what-does-queue-management-add.md) (Adding)
and [Does adding a folder need a background probe?](../../202609-faderio-phase-1-restage/issues/07-does-adding-a-folder-need-a-background-probe.md).
Carries v2 0003's intent.

## What to build

Today's `AddTracks` probes and reads tags under the engine lock that the audio thread takes,
so adding during playback stalls the audio. Replace it with **`Add(paths)`**:

- It returns at once.
- It probes and reads tags off the lock and off the UI thread.
- It appends rows at the end, in the given order, as files resolve.
- The view shows progress.
- **Cancel** keeps the rows already added.
- A second add waits behind the first.
- Rejections arrive as one notice on completion.
- Adding never starts playback.

This issue takes **files only**, through the existing **Add files…** button and launch
arguments. Folders and playlists are issues 02 and 03.

## Where the logic lives

- **Engine:** `Add`, `CancelAdd`, the add's progress in `PlayerView`, and appending under
  the lock only for the moment a row is inserted.
- **Shell:** progress and a Cancel control in the status strip. It formats the view's progress
  and computes nothing.

## Acceptance

- [ ] While a track plays, adding 50 files does not hold the engine lock for any probe; only
      the row appends take it.
- [ ] Rows appear in the given order as they resolve; the view's progress counts them.
- [ ] Cancel mid-add stops further probing and keeps the rows already added.
- [ ] A second `Add` issued mid-add runs after the first, in order.
- [ ] Rejections are reported once, on completion, with each reason (ADR 0007).
- [ ] Adding to an empty, stopped queue does not start playback.
- [ ] `AddTimeBudgetTests` still pass.

## Tests

- **fake:** order, progress, cancel, queuing and rejection reporting. **Probing off the lock:**
  prove the audio thread can take the lock while a probe is in flight, for example with a
  probe seam that blocks until released.
- **form:** progress appears and Cancel is wired.

## Smoke

- **Looks:** while adding a folder's worth of files, the window stays responsive and the strip
  shows progress and a Cancel.
- **Sounds:** add the whole `MUSIC` library while a track plays; the track plays on without a
  dropout.
