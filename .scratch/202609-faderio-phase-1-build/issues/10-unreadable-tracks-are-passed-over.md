# 10: Unreadable tracks are passed over

Status: ready-for-agent
Blocked by: 06, 09

Decided in [What happens when a file or the device fails?](../../202609-faderio-phase-1-restage/issues/05-what-happens-when-a-file-or-the-device-fails.md)
(the unreadable, incoming and decode-error sections). **Unreadable** is in `CONTEXT.md`.
Carries v2 0014's intent (story 36). Closes audit findings 9 and 10.

## What to build

- **An unreadable track is passed over with a notice.**
  - `QueueRow` gains the unreadable mark, and the row stays in the queue.
  - The track is tried again whenever the queue reaches it, and the mark clears when the file
    opens.
  - ⏭, ⏮ and Transition now pass over it.
  - Double-clicking it tries it once. If that fails, the playing track carries on.
- **The incoming track opens when its boundary is scheduled.** If it fails, it is marked and
  the boundary is re-targeted to the next readable row, whose adjacency resolves as usual.
- **A decode error mid-track is treated exactly like audio that ends early** (ADR 0010):
  - one notice;
  - the track ends there;
  - the boundary fires there.

  This replaces the per-block silence and notices in `EngineFrameSource.Read`.

## Where the logic lives

- **Engine:** all of it. The shell renders the mark from `QueueRow`.

## Acceptance

- [ ] A queued file deleted before its turn is passed over with one notice. Its row stays,
      marked. Restoring the file and reaching it again plays it and clears the mark.
- [ ] With A→B authored and B deleted, A never fades into silence; the boundary goes to the
      next readable row with that adjacency's own resolution.
- [ ] A file that fails to decode mid-stream produces one notice and a boundary at that point.
- [ ] ⏭ and ⏮ pass over marked rows; double-clicking a marked row that still fails leaves the
      current track playing.

## Tests

- **fake:** each case. A decode error can be simulated with a fixture truncated after its
  header plus a seam that fails reads at a given frame; choose the smallest seam that is
  honest.
- **form:** the mark is rendered.

## Smoke

- **Looks:** an unreadable row is visibly marked.
- **Sounds:** rename a queued file before its turn; the queue plays on past it without a gap
  or a fade into silence.
