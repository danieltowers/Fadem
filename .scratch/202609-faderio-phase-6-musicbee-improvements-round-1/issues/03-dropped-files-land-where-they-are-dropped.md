# 03: Dropped files land where they are dropped

Status: ready-for-agent
Blocked by: 02

Phase 6, MusicBee improvements round 1. Raised 2026-10-02: tracks dropped on the queue, from
MusicBee (issue 01) or Explorer, always go to the end; they should land where they are dropped.

## The cause

`Engine.Add` only appends. The shell cannot place dropped files itself: adding, then moving the
rows once they resolve, is two engine calls for one intent and holds the new rows between them
(CLAUDE.md's first and third tests). The engine's Add takes the place.

## What to build

Files dropped on the queue, in either host, show the drop line as rows do and land where it
was, in the drag's order. **Add files…**, **Add folder…**, the MusicBee context item and
imports still append.

## Implementation decisions

- **The engine.** `Add(paths, RowId? before)`, the end when null, as `Move` takes its place.
  The existing `Add(paths)` is the end. Each add job carries its own anchor, so a second drop
  during a first lands where it was dropped.
- **Order kept.** A job's files are inserted one after another as they resolve, each after the
  job's last inserted row (before the anchor for the first), so a folder or playlist dropped
  keeps its order, and rows from other jobs never interleave inside it.
- **The anchor goes.** If the anchor row is removed before the job's first row lands, the job
  goes to the end. Once one row has landed, the rest follow it. No notice: the drop was
  accepted, and where its rows go is plain to see.
- **Shuffle.** Under shuffle the rows land at the drop point in the visible order, which is
  the play order, as `Move` places rows. In the order that turning shuffle off returns to, they
  go at the end, as an appended row does.
- **The passing row** (one still sounding from the playlist open before) stays at the top: a
  drop above it lands just below it, as `Move` treats it.
- **Playback.** As for `Move`: if the inserted rows change which row comes next, the pending
  boundary follows, unless its transition has started. Adding never starts playback.
- **The shell.** Both hosts' file drops go through issue 02's shared drag piece. The drop line
  shows for files as for rows, and the drop is one `Engine.Add(paths, before)`.

## Testing decisions

- Core, on both targets: files added before a row land there in order; a folder dropped before
  a row keeps its order; two adds with different anchors each land at their own; the anchor
  removed before the first row lands sends the job to the end, removed after keeps the rest
  together; under shuffle the visible order has them at the point and turning shuffle off puts
  them at the end; a drop above the passing row lands below it; an insert before the next row
  moves the pending boundary, unless its transition has started.
- `Faderio.Shell.Tests`: a file drop makes one `Add` with the row at the drop index.
- The App's and the plugin's drop tests gain a drop between two rows, landing there.

The Looks half (both sections) gains an item, the user's (a real mouse): a track dragged from
MusicBee, or files from Explorer, onto the middle of the queue shows the line and lands there.

## Acceptance criteria

- [ ] Files dropped on the queue land at the drop line, in order, in both hosts.
- [ ] One drop is one engine call.
- [ ] Menu adds, the context item and imports still append.

## Comments
