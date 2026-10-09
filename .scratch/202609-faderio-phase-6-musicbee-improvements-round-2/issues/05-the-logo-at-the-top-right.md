# 05: The logo at the top right

Status: ready-for-agent
Blocked by: 03

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: "add fadem logo to top right. scale to
reasonable size. dark and light versions are in the assets folder".

## What to build

Show the product's logo at the top right of the view. The art is in `assets/`: `logo-white.png`
for a dark palette and `logo-black.png` for a light one (`Palette.IsDark` decides), both 1323 ×
534 px with transparency, so about 2.5 : 1.

Scale it down once, with high-quality interpolation, to a height that sits in the top line
(about 20–24 px, so about 50–60 px wide), and draw it there. Embed the two images as resources
of the shared shell, not files beside the exe or plugin, so the plugin's install stays two items.

The product is being renamed to Fadem (public release issue 02); the art already says Fadem.

## Where (decided 2026-10-09)

**Both hosts.** In the plugin, at the right of the track's line, where ⚙ is today (issue 03 moves
⚙ to the status strip). In the App, at the right of the top toolbar row, where Settings… is today
(issue 03 removes it). The size was not settled: start at 20–24 px tall in both and show the user.

## Acceptance

- [ ] The logo shows at the top right as decided, white on a dark palette and black on a light one.
- [ ] Crisp at 100 % and at 150 % display scaling; not stretched.
- [ ] In MusicBee's sidebar it does not push the track's name to nothing; the name is cut short first.
- [ ] Both images are embedded; the plugin's install is still `mb_faderio.dll` and `faderio\`.
