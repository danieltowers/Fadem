# 13: ReplayGain switches live

Status: resolved
Blocked by: 12

Decided in [When does ReplayGain switch, and is it on by default?](../../202609-faderio-phase-1-restage/issues/06-when-does-replaygain-switch-and-is-it-on-by-default.md).
Carries v2 0012's intent. Closes the ReplayGain part of audit finding 14.

## What to build

- **The ReplayGain checkbox** goes in the Settings dialog. It is on by default.
- **Toggling applies at once to every live voice,** including preview voices once issue 16
  lands. Each voice's base gain ramps to its new value over a few milliseconds, so there is no
  click.
- **Gain stays per voice.** An untagged track gets 0 dB. It is track gain only; the peak tag is
  not read.
- Today the gain is fixed when a voice is created. It becomes changeable.

## Where the logic lives

- **Engine:** the ramped change on live voices, reached through `ChangeSettings`.
- **Shell:** one checkbox in the existing dialog.

## Acceptance

- [x] Toggling mid-track changes the playing track's gain within one device buffer, with no
      discontinuity in the rendered samples.
- [x] Mid-crossfade, each voice moves to its own tag's gain.
- [x] An untagged track is unaffected by the toggle.

## Tests

- **fake:** the rendered-block gain before and after a toggle, the ramp's continuity, and the
  crossfade case. The existing `ReplayGain_*` tests are kept.
- **form:** the checkbox reaches `ChangeSettings`.

## Smoke

- **Sounds:** on a tagged track (the library has two; tag one with an external scanner if
  needed), toggling ReplayGain changes the level at once, with no click.
