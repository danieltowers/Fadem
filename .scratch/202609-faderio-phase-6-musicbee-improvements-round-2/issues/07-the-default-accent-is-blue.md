# 07: The accent is blue, on every skin

Status: ready-for-agent
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: "change default highlight colour to
the original blue instead of red".

## What is there today

`Palette.From` takes the accent from the skin when the skin answers one with enough saturation
(the plugin reads MusicBee's element 7, state 4 in `Host.ReadPalette`); otherwise it falls back to
amber (`Amber` on a dark skin, `DeepAmber` on a light one). On the user's skin MusicBee answers
red, so the plugin is red; the App uses the palette the plugin last wrote, so it is red too.
Before the studio theme the seek bar drew in `SystemColors.Highlight`, Windows' selection blue:
that is "the original blue".

## What to build (decided 2026-10-09, grilling)

- **The accent is a fixed `#0078D7`** (Windows' default selection blue), not the live system
  colour, so it never changes with Windows' settings.
- **On every skin.** faderio stops taking the skin's accent: `SkinColours.Accent` is no longer
  read for it (the plugin can stop reading element 7, state 4, or keep reading and ignore it).
  The skin still gives the chrome, wells, edges, keys, text and dim.
- **The same blue on dark and light skins**, no deeper variant. The palette's contrast nudge
  (toward the text while contrast with the Well is under 3) stays, for a skin whose Well makes the
  blue unreadable. `Amber` and `DeepAmber` go.
- `Palette.Mark` stays red: it is red unless the accent is near red, and blue is not.
- The App gets the blue through the palette file the plugin writes, as now, and from
  `Palette.Studio` when there is none.

## Acceptance

- [ ] The plugin on the user's red-accent skin shows `#0078D7` wherever the accent is used (as
      issue 06 leaves those uses), and so does the App after it.
- [ ] Dark and light skins alike; Duophonic, MusicBee3 and CalciumMETRO checked in the portable
      MusicBee (memory: disruptive MusicBee checks go in the portable).
- [ ] `PaletteTests` pin the accent at `#0078D7` whatever the skin answers, and the nudge on a
      Well where it fails contrast.
- [ ] The studio theme spec's and `docs/smoke.md`'s wording about the skin's accent updated.
