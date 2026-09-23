# What completes the editor?

Type: grilling
Status: open
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
