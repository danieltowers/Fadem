# 01: A line between MusicBee and faderio

Status: resolved
Blocked by: none

Phase 6, improvements round 2, MusicBee only. Raised 2026-10-09: "add a boundary line at the top
between musicbee and fadem".

## What to build

The plugin's view (`PluginView`) meets MusicBee's own strip at its top edge with nothing between
them, so the two run together. Draw a one-pixel rule across the top of the view in the palette's
Edge, the colour the studio theme already uses for hairlines and frames. It spans the view's
whole width, in the sidebar and in the Now Playing tab alike.

## Acceptance

- [ ] A one-pixel Edge rule runs the full width of the plugin's top edge, in the sidebar and the
      Now Playing tab, on a dark and a light skin.
- [ ] Nothing below it moves by more than that pixel; the editor's height budget (memory: editor
      panel height budget) is not pushed into a scroll.
- [ ] Looks smoke item for the MusicBee view extended to name the rule.

## Comments

### 2026-10-09, built

The view keeps a pixel of top padding and paints it in Edge (`PluginView.OnPaint`). On the user's
skin that is `#0F0F0F` on `#1C1C1C`, a subtle line; the user kept it as it is.
