# 05: The spike's verdict

Status: ready-for-human
Blocked by: 03, 04, 06

Part of the [MusicBee spike](../spec.md). This ticket answers the [brief](../brief.md).

## What to build

The user runs the Sounds half of the MusicBee smoke in the portable MusicBee:

- The open playlist's authored crossfade sounds as it does in standalone faderio.
- MusicBee's own player plays again straight after faderio's Stop, in WASAPI shared.
- The same holds in WASAPI exclusive.
- Playback holds with MusicBee's other plugins enabled.

Then the outcome is recorded.

**If every point passes:**

- ADR 0012 becomes `accepted`.
- **Host** enters `CONTEXT.md`: the program playing a library, either faderio itself or MusicBee
  through the plugin.
- The result is appended to the brief.
- The later phase 5 issues are staged in this folder, blocked by this one:
  - Core's whole suite on net48
  - the shared WinForms library
  - the plugin view: playlists, queue, transport
  - the transition editor and preview in MusicBee
  - the transition manager in MusicBee
  - adding MusicBee's selection to the open playlist
  - the output device setting
  - one library holder at a time

**If any point fails:**

- Append to the brief which point failed, what was tried and what was heard or seen.
- ADR 0012 becomes `rejected`, and the v3 map's MusicBee line points to the reason.
- If the failure is another plugin's copy of a dependency winning the load, try the recorded
  fallback (ILRepack, internalised) before rejecting.

## Acceptance criteria

- [ ] The user has run each Sounds item and reported it.
- [ ] ADR 0012's status, `CONTEXT.md`, the brief and (on a pass) the staged issues match the
      outcome.
