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

## Comments

- **The agent's half, 2026-09-28.** 03, 04 and 06 are resolved, and the MusicBee Looks half
  passes on the build of issue 06 (its comments list each item). The Sounds half is yours.
  - The plugin is installed in the portable MusicBee, its settings naming the smoke library
    `faderio-smoke\library` (two tones, a 2 s
    crossfade at 6 s) in WASAPI shared. Point it at a real library in Configure… for the ear.
  - For exclusive: choose it in Configure…, Save, restart MusicBee. Expect at every start
    "WASAPI exclusive unavailable (Not implemented); trying WASAPI shared." (the device is
    held at that moment; see 06). Play still opens exclusive: with the DragonFly the probe
    was refused while faderio played, and opened again after Stop.
  - Expect one notice per crossfade, "… ends N s before its header claims." That is the
    engine's, [issue 07](07-an-authored-fade-out-is-reported-as-the-track-ending-early.md),
    not the plugin's. Weigh both notices at the verdict.
- **The Sounds half so far, 2026-09-30**, in the portable MusicBee on the build of issue 07
  (`c8f260b`), default device "Speakers (C-Media(R) Audio)", 48 kHz. The user listened; the
  agent timed the device.
  - ✅ **The authored crossfade** sounds as in standalone faderio, and no notice shows: issue
    07's fix holds in MusicBee.
  - ✅ **WASAPI shared:** MusicBee's own player plays straight after faderio's Stop, and
    faderio's Play stops MusicBee's player; the two never sound together.
  - ⚠️ **WASAPI exclusive:** the crossfade plays. MusicBee's own player, started at once
    after faderio's Stop, shows MusicBee's "unable to start playback"; started three seconds
    after, or tried a second time, it plays. Not yet judged pass or fail: the user's call.
    - faderio is not holding the device in that gap. Driven by the menu walk and timed with
      the exclusive probe, the device was refused (0x8889000A) while faderio played, and free
      about 0.2 to 0.3 s after the Stop key, on two rounds. The Stop path closes the player on
      the UI thread before the command returns.
    - An exclusive stream opened and closed by a PowerShell outside MusicBee can be followed
      at once by another stream, shared or exclusive, in that process (0 ms, three rounds
      each). MusicBee's own player, double-clicked as such a stream closed, played; that one
      round was disturbed by a probe of the agent's and is weak evidence.
    - So the pause looks like MusicBee's player recovering from an exclusive stream in its
      own process, not a late release. Not proven: MusicBee's player cannot be started by
      automation.
  - **Seen once, cause not confirmed:** with exclusive chosen, a Play showed "WASAPI exclusive
    unavailable (Not implemented); trying WASAPI shared." and played shared. MusicBee's own
    player had most likely been playing until that Play stopped it, the same condition as the
    notice at start.
  - **Seen, the agent's probe being the cause:** after "Audio output lost" (an exclusive probe
    evicting faderio's shared stream), Stop then Play opened shared without a notice, and the
    Play after that opened exclusive again. `Reopen` opens from the mode in use, which the
    fallback at start had left as shared.
  - **The endpoint meter** of the C-Media device hears faderio in shared (0.465) and not in
    exclusive, as on the DragonFly.
  - ⬜ **Other plugins enabled:** Theater Mode, SkipToNearEnd and DummyTrackController were
    enabled throughout (DummyTrackController logged track changes during the run). The user
    has not yet been asked to confirm this item.
