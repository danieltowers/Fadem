# What completes the editor?

Type: grilling
Status: resolved
Blocked by:

## Question

The slice's transition editor authors all four parameters, from the start only, with the
equal-power curve fixed. v2 issue 0009 and v2's editor had more. Decide which come back:

- **From-end points.** v2 offered from-end on the fade-out point only. ADR 0010 says from-end
  points resolve against `Length`. Offer it on the fade-out point, both points, or neither?
- **Curve selection.** Equal-power by default and linear selectable, per transition (v2 0007).
  Keep, or keep equal-power fixed?
- **Delete versus reset.** v2's "Reset" cleared rather than reverted. The engine has
  `Unauthor`. What does the editor offer: revert unsaved edits, unauthor the adjacency, both?
- **Entry points.** The row context menu opens "this → next". Is that enough, or does the
  editor need a way to reach the previous boundary or the wrap boundary (ticket 02)?

## Comments

- From [What is preview?](04-what-is-preview.md): the editor gains **Preview** / **Preview
  again** / **Stop preview**, and Capture reads the preview's voices while one runs. That
  makes capturing a fade-in point possible without playing the incoming track in the queue.
  Weigh the entry-point question with that in mind.

## Answer

Grilled with the user on 2026-09-23, starting from the audit in
[Audit the engine against v2's intent](01-audit-the-engine-against-v2s-intent.md). `CONTEXT.md`
amends **Adjacency**: a track can be adjacent to itself (A→A) when it repeats.

### From-end points: on the fade-out point only

- "Start fading 12 s before the end" is how an outro is heard, and there is now one `Length` to
  measure it against (ADR 0010). From-end is not offered on the fade-in point: the resolver
  ignores it there, and nobody places an intro by its distance from the end.
- **Capture into a from-end field** stores `Length − playhead`, computed by the engine
  (placement test 2).
- **Ticking from-end converts the existing value** so it keeps the same moment. On a 3:40 track,
  3:12 from the start becomes 0:28 from the end. The engine does the conversion.
- Whether a from-end point on an MP3 moves if trimming is adopted belongs to
  [Trim tagged MP3s, or keep whole frames?](13-trim-tagged-mp3s-or-keep-whole-frames.md).

### Curve: selectable, one per transition

Equal-power by default, with linear selectable. The resolver and mixer already honour
`TransitionSpec.Curve`, so this is one control. Linear suits a same-material overlap (a live
album's crowd, a DJ-style blend), where equal-power bumps the overlap about 3 dB and would
light the clip light.

### Revert and Remove transition

- **Revert** discards unsaved edits and reloads the fields from the store.
- **Remove transition** calls `Unauthor`. The adjacency then resolves as unauthored. The
  removed values stay in the fields as an **unsaved draft**, so **Save** restores them. That is
  undo without a confirmation dialog.
- **Saving with every field blank calls `Unauthor`** as well, so the store never holds an
  empty transition. This fixes the audit's finding that a blank save wrote an empty spec.

### Entry points: every adjacency that can fire

- **This → next** follows the same target rule as ⏭ and Transition now
  ([What do skip, repeat and end-of-queue do?](02-what-do-skip-repeat-and-end-of-queue-do.md)).
  From the last row it targets last → first under repeat-all, and it is disabled with repeat
  off. Under shuffle, "next" is the visible next row.
- **Previous → this** is added: the boundary into a row. With preview, this is how the
  transition into a track you are listening to gets authored.
- **This → this** is added while repeat-one is on: the self-adjacency that fires at the
  track's natural end.
- The engine supplies each target. The shell never computes "next" or "previous".
- An adjacency picker (two dropdowns) was rejected: it would author pairs that are adjacent in
  no queue, which is a library feature.

### Leaving with unsaved edits

Opening another adjacency or closing the editor with unsaved edits asks **Save / Discard /
Cancel**. With no unsaved edits it asks nothing. Silent discard loses points captured by ear.
Silent save commits transitions that were only previewed.

### Validation in the engine

`Author` refuses impossible values and returns what it refused and why, rather than throwing
(every command stays total):

- a fade-out point past the outgoing track's `Length`;
- a fade-in point past the incoming track's `Length`;
- a from-end point longer than its track;
- a negative duration.

The editor shows each reason next to its field. A duration that only runs past `Length` is
still shortened by the resolver without complaint: that is a resolution, not an error. Only the
engine knows both lengths, so validation lives there (placement test 2).

### Consequences for other tickets

[Which settings exist, and where do they live?](10-which-settings-exist-and-where-do-they-live.md)
gets nothing from this ticket.
