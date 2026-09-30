# 13: The output device setting

Status: needs-triage
Blocked by: 05

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 8, ADR
0012). Staged at the verdict; it needs its own spec before an agent builds it.

## What to build

A per-host output device setting in the plugin's storage, defaulting to MusicBee's current
device, beside the output mode the spike already has. The spike always opens the default
device.

## Carried from the spike's verdict

Found in the Sounds half, 2026-09-30 (the comments of [05](05-the-spikes-verdict.md) have the
measurements). Each wants a decision here:

- **The notice at every start in exclusive.** "WASAPI exclusive unavailable (Not implemented);
  trying WASAPI shared." shows whenever MusicBee starts with exclusive chosen, because the
  device is held at that moment. Play opens exclusive anyway, so the notice is untrue by then.
- **Play while MusicBee's own player plays.** Seen once: faderio's Play, which stops MusicBee's
  player first, could not open exclusive and fell back to shared with the same notice. Cause
  not confirmed; a short retry after `Player_Stop` is the obvious thing to try.
- **The mode after a device loss.** `Reopen` opens from the mode in use. After a fallback that
  is shared, so the Play after a loss opens shared without a notice although exclusive was
  asked for. The Play after that opens exclusive again.
- **No mode shown.** The plugin has nowhere to say which mode is in use (the view of
  [10](10-the-plugin-view.md) may be where).
- **The known limit, accepted.** In exclusive, MusicBee's own player shows "unable to start
  playback" if started at once after faderio's Stop, and plays a few seconds later. The device
  is free about 0.2 to 0.3 s after Stop, so this is on MusicBee's side. Nothing to build unless
  a cause is found.

## Acceptance criteria

- [ ] The listener chooses the plugin's output device, and it is remembered per host.
- [ ] Each carried item above is fixed, or recorded as decided against.
- [ ] The MusicBee smoke covers the setting, and its exclusive-probe item still passes.
