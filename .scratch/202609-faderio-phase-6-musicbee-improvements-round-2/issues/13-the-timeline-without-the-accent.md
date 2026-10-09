# 13: The timeline without the accent

Status: needs-triage
Blocked by: none

Phase 6, improvements round 2, both hosts. Split from issue 06 on 2026-10-09: the user raised
"remove highlight colour from track timeline", and deferred what the timeline becomes.

## What is there today

The seek bar (`SeekBar`, shared) draws the played part of the groove and the thumb in the accent
(`Palette.Accent`, blue after issue 07), and the fade wedges in `Palette.Fade`, a mix of the accent
and the chrome. The fade marks are `Palette.Mark`, red.

## To decide

What the played part, the thumb and the wedges become without the accent. Options put in the
grilling:

- (a) played part in Dim, thumb in Text, wedges a neutral tint (Text mixed into the chrome);
- (b) played part and thumb in Text, wedges neutral;
- (c) played part and thumb neutral, wedges keep the accent's tint.

The recommendation was (a): played in Dim reads apart from the darker unplayed groove, the thumb in
Text is the brightest thing on the bar, and neutral wedges leave the red marks as the bar's only
colour. Not yet decided.

## Acceptance

- [ ] As decided; the played part stays distinct from the unplayed groove and from the wedges, on
      a dark and a light skin.
