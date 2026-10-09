# 12: A better icon for Cue transition

Status: ready-for-agent
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: "Cue transition button needs a
better icon."

## What is there today

The Cue transition button plays from 5 s before the coming transition. It shows ⏩ (fast forward,
Segoe UI Symbol), which reads as "skip ahead" or "faster", not "go to just before the
transition". Its neighbour, Transition now, shows ⤳.

## What to build

A glyph or a small drawn icon that says "jump to just before the transition". Candidates to choose
from in triage, all drawable in the transport's 31 × 28 button:

1. **⇥ (rightwards arrow to bar)**: go to a point.
2. **⏭ with a fade wedge**: drawn, not a font glyph; matches the seek bar's wedges.
3. **↦ followed by the ⤳ of Transition now**, drawn small: "go to, then transition".
4. **A drawn wedge with a playhead line before it**: the seek bar's own picture of a cue.

**Chosen 2026-10-09: 1, ⇥.** Check it renders in Segoe UI Symbol at the button's size, centred
as the other transport glyphs are; if it does not, say so before drawing a substitute.

A drawn icon (2 or 4) follows the palette like the seek bar; a font glyph (1, 3) follows the
button's text colour. Its tooltip stays "Cue transition: play from 5 s before it".

## Acceptance

- [ ] The chosen icon, in both hosts, legible at the button's size on a dark and a light skin, and
      greyed when the button is disabled.
