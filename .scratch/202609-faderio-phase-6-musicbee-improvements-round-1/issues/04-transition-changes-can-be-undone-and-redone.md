# 04: Transition changes can be undone and redone

Status: resolved
Blocked by: none

Phase 6, improvements round 1, both hosts. Raised 2026-10-02 as half of "lose the save button,
changes in the transition panel should be saved automatically (needs undo though)". This issue
is the undo; 05 is the editor saving itself, which needs it.

## What to build

The engine keeps one history of this session's transition changes, and steps back and forth
through it: `Undo()` and `Redo()`. Every change to the store from authoring is a step: a save,
a save that removes, Remove transition, an override saved or removed. The history is the
engine's, not the shell's (CLAUDE.md's third test), so both hosts and any number of views
share it.

## Implementation decisions (settled with the user, 2026-10-02)

- **Scope: every transition change this session**, whichever transition it was on, in one
  history. Not per transition, not kept across restarts.
- **A step** records the adjacency, the layer it changed (the global transition, or the named
  playlist's override), and what that layer held before and after. Only a call that changed
  the store makes a step: a refusal, a failed write or "nothing to save" makes none.
- **Undo** puts the layer back as it was before the newest step, **Redo** as it was after the
  step undone last. They write what was stored, unvalidated: it was valid when stored. A new
  change after an undo drops the steps that could be redone.
- **Playback** follows as for any save: a pending boundary of that adjacency is rescheduled,
  one in flight plays out.
- **Each returns the adjacency it changed**, or null with nothing to undo or redo, so the
  editor can open it. `PlayerView` carries what Undo and Redo would change next (null for
  nothing), for the buttons' enabled state and tooltips, which the Presenter words.
- **Notices.** "Undone: *what the step did*." and "Redone: …", in the words of the notice
  the change gave (Saved / Removed …), so the strip says what moved.
- **The history ends** with the library: opening another library clears it, since it names the
  store left. A failed undo or redo (the store cannot be written) is a notice, and the step
  stays where it was.
- **Playlists.** Renaming a playlist renames it in the history's steps. Deleting one drops
  its steps, since their overrides went with it.

## Testing decisions

Core, on both targets, through the engine:
- Save, Undo: the store holds what it held before and the view says Redo would redo it; Redo
  puts the save back.
- Two saves on different adjacencies undone in reverse order, each returning its adjacency.
- Remove transition, Undo: the transition is back. An override saved, Undo, Redo.
- A change after an undo drops the redo.
- A refused save and "nothing to save" make no step.
- Undo while that adjacency's boundary is pending reschedules it (the fade audible as it
  was); one in flight plays out.
- Opening another library clears the history; a renamed playlist's step undoes under the new
  name; a deleted playlist's steps are gone.
- The notices name what was undone and redone.

## Acceptance criteria

- [x] Every authoring change to the store can be undone and redone, in one session history.
- [x] The view says whether there is something to undo or redo.
- [x] Undo and redo reschedule as a save does.

## Comments

- **Resolved 2026-10-02.** Built as specified above; the editor's use of it is issue 05.
  - **The engine:** `PlaybackEngineHistory.cs`. `Undo()` and `Redo()` return the adjacency
    changed, or null. A step (`HistoryEntry`) is the adjacency, the playlist (null for the
    global layer) and the layer's spec before and after; `Author`, `AuthorOverride` and the
    removals record one after their write succeeds, and a write that left the layer equal
    records none. `PlayerView.NextUndo` and `NextRedo` are `HistoryStep`s, whose `Describe()`
    words the notices ("Undone: removed A → B.") and will word the buttons' tooltips. Opening a
    library clears the history; renaming a playlist renames its steps; deleting one drops them.
  - **Tests:** `AuthoringHistoryTests`, 13 on both targets: a save undone and redone, the first
    save undone to nothing, two adjacencies newest first, Remove transition and a blank save
    undone, an override and its removal, a change dropping the redo, refusals and same-value
    saves making no step, a pending boundary rescheduled and one in flight playing out, the
    notices, a library switch, a playlist renamed and deleted. With the reschedule removed, the
    pending-boundary test fails.
  - No shell change, so no smoke: nothing visible until 05.
