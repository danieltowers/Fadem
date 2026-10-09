# 05: The logo at the top right

Status: resolved
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

## Comments

### 2026-10-09, built

`Logo` in the shell, a control both hosts dock at the right: the App in a cell at the command
row's right, level with its buttons; the plugin at the track's line's right, docked after the
name, so the name ends in "…" before it. Both PNGs are embedded in `Faderio.Shell.dll`. The
art's transparent margin is trimmed at load (it is about a fifth of the height), then scaled
once, high-quality bicubic, to 22 px tall, about 61 px wide, and drawn pixel for pixel. The
mark is in colour in both arts; only the wordmark is white or black.

Not checked: 150 % scaling by eye. There the rows keep their fixed pixel heights, so the logo
stays 22 px tall (crisp, smaller to the eye); its width follows the parent's height at layout,
so the slot is never wider than the art (`LogoTests`, a short parent).

### 2026-10-10, three times the size

The user: "logo should be 3x size". `Logo.LogicalHeight` is 66. A row could not hold that, so the
logo has a column of its own at the top right: in the App beside the command row and the track's
line (the header is 70 px, 8 more than the two rows; the track's line fills the extra, so it
still meets the transport); in the plugin's main panel beside the track's line and the buttons
(about 6 px more). In a sidebar the 182 px-wide logo would take most of the 284 px, so it stays
on the track's line there, shrunk to 22 px. The App's editor, which already scrolled a little at
1240 x 660, is 8 px shorter.

