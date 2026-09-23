# What do skip, repeat and end-of-queue do?

Type: grilling
Status: resolved
Blocked by: 01

## Question

v2 issue 0008 settled these rules against v2's engine; decide which hold on v3's seam, using
ticket 01's audit for what the engine does today:

- **Skip during a transition.** Abandon the in-flight transition and start the next adjacency
  with its own, or let the outgoing fade finish first? v2 had both, behind a setting.
- **Skip and Previous at all.** What Previous does mid-track versus near its start.
- **Repeat-all.** The wrap boundary as a real adjacency (last → first).
- **Repeat-one.** Fire an explicitly authored self-adjacency (A → A), or restart plainly.
- **Shuffle.** v2 said transitions fire per adjacency as encountered; confirm, and say what
  the editor's "this → next" means under shuffle.
- **End of queue.** The authored end-of-queue fade-out (`FindForOutgoing`), cut from the slice.
- **Audible time.** Whether queue advance and the end-of-track signal follow the playhead or
  the decode position (ADR 0001).

Each rule must be one engine call per user intent (placement test 1).

## Answer

Grilled with the user on 2026-09-23, starting from the findings in
[Audit the engine against v2's intent](01-audit-the-engine-against-v2s-intent.md). Each rule
below is one engine call per user intent. `CONTEXT.md` was updated in this session: **Skip**
was sharpened and **Transition now** was added.

### Skip (⏭)

- **During a transition**, a skip lands on the in-flight adjacency's incoming track at full
  volume. The outgoing voice drops out instantly. A skip never passes over the incoming track;
  pressing ⏭ a second time does that.
- **The let-finish setting is dropped.** `SkipBehaviour` and its test go. A skip means "now";
  a smooth exit is what **Transition now** is for.
- **Outside a transition**, ⏭ hard-cuts to the next track **from 0:00**. It ignores the next
  adjacency's authored fade-in point, which is today's behaviour.
- **On the last track with repeat off**, ⏭ stops.
- **Under repeat-one**, ⏭ moves to the next row and repeat-one stays on, so that row now
  loops. This fixes today's behaviour: `IndexAfter` with repeat-one returns the same index, so
  ⏭ restarts the track. Repeat-one governs only a track's natural end.
- **While paused**, ⏭ lands on the next track at 0:00 and stays paused, which is today's
  behaviour.

### Transition now (new)

A new button and a single engine command. It starts the current adjacency's transition
immediately.

- **Target.** The same adjacency ⏭ would reach: the next row under repeat-one, and the
  last → first wrap under repeat-all on the last row.
- **Shape.** Only the fade-out point moves, to the playhead. The fade-out duration, fade-in
  point, fade-in duration and curve play as authored, and the incoming voice spawns now, as it
  would have at the authored point. If the fade-out would run past the outgoing track's
  `Length`, the duration is shortened, as the resolver already does. With no fade-out authored
  (the non-overlapping shape), the outgoing track stops now and the incoming one starts at its
  fade-in point with its fade-in.
- **Unauthored adjacency.** It resolves as the natural boundary would: a hard cut (so it
  behaves like ⏭, from 0:00), or the global default crossfade started now if that setting
  exists and is on. It is never disabled because a boundary is unauthored.
- **When it does nothing.** It is disabled, and does nothing, in three cases: while a
  transition is in flight, when there is no next adjacency (last row, repeat off), and while
  paused or stopped. The shell reads all three from the view (`Incoming`, `Transport`, the
  queue and `Repeat`) without arithmetic.

### Previous (⏮)

- **Past 3 s** on the attended playhead, ⏮ restarts the track. Otherwise it hard-cuts to the
  previous row **from 0:00**. It never honours a reverse adjacency: B→A's transition is for
  playing B then A, not for going back.
- **At the head of the queue**, ⏮ restarts the track. Under repeat-all it wraps to the last
  row instead. This is new; today `PreviousIndex` ignores repeat.
- **During a transition**, ⏮ acts on the attended (outgoing) voice, as today: restarting it
  cancels the transition, the same as a seek.

### Repeat and shuffle

- **Repeat-all.** The wrap is a real adjacency, last → first. Confirmed; this is today's
  behaviour and is tested.
- **Repeat-one.** It fires an authored A→A self-adjacency, or restarts plainly without one.
  Confirmed; this is today's behaviour and is tested.
- **Shuffle.** Transitions fire for each adjacency as the shuffled order produces it.
  Confirmed. The visible order is the play order: shuffle reorders the rows, so the editor's
  "Edit transition (this → next)" names the adjacency that will actually play. Turning shuffle
  off restores the original order. This is today's behaviour.

### End of queue

- **The end-of-queue fade-out is cut.** With no next adjacency, the last track plays to its
  `Length` and playback stops. `ITransitionStore.FindForOutgoing`, the `ScheduleBoundary` branch
  that uses it, and `EndOfQueue_AuthoredFadeOutPlaysOut_ThenPlaybackStops` all go.
- An explicitly authored "A → end" adjacency was considered and ruled out of this map. See the
  map's Out of scope section.

### Audible time (ADR 0001)

- **Every signal the shell sees follows the playhead.** Queue advance already does: the
  current row changes at the audible handover. **Stopped does not yet.** It is published when
  the last voice has been rendered, about one device buffer early. It must instead be published
  once the device has emitted the last frame.
- The build issue adds a fake-seam test that the final buffer is not cut and that *Stopped*
  follows the playhead, plus a Sounds line in `docs/smoke.md` for the last track's tail.

### Knock-on

- [Which settings exist, and where do they live?](10-which-settings-exist-and-where-do-they-live.md)
  loses skip behaviour. Whether the global default crossfade survives is still its question;
  **Transition now** follows whichever answer it gives.
- [What happens when a file or the device fails?](05-what-happens-when-a-file-or-the-device-fails.md)
  inherits these rules for a broken incoming track.
- No ADR: every rule here is cheap to reverse.
