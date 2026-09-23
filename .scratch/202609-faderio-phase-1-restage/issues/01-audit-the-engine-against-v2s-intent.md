# Audit the engine against v2's intent

Type: task
Status: open
Blocked by:

## Question

Nothing to decide: find out how much of Phase 1 the engine already holds. For each in-scope
feature on the map, record three facts against v2's issues 0003, 0008, 0012, 0013 and 0014 and
v2's spec:

- **Present?** Which `PlaybackEngine` commands and `View` fields already serve it
  (`Remove`, `Clear`, `Next`, `Previous`, `PlayRow`, `SetShuffle`, `SetRepeat`, `Unauthor` are
  known to exist).
- **Tested?** Which tests reach it, and at which of ADR 0002's six seams.
- **Reached by the shell?** Whether any control, menu item or launch argument calls it.

Then list where v2's intent is missing or differs: for example folder add, reorder, M3U, skip
behaviour while a transition is in flight, repeat-one's self-adjacency, the authored
end-of-queue fade-out, a broken file mid-queue, the WaveOut fallback.

Agent alone. The answer is a table in this ticket, not code. Tickets 02 and 05 wait on it.
