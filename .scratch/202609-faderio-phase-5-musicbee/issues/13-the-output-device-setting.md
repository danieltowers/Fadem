# 13: The output device setting

Status: resolved
Blocked by: 05

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 8, ADR
0012). Staged at the verdict; specified below (2026-10-01) before it was built.

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

## Implementation decisions

- **The setting.** `MachineSettings` gains `OutputDevice`: the device by its name in Windows
  (the endpoint's friendly name, as MusicBee's own preferences name devices), or null for the
  host's default. The schema stays 2: a null key is nothing to an older file, and a file the
  App and the plugin never share. Standalone faderio leaves it null and opens Windows' default
  device, as it does today; its settings form keeps the value it opened on, so nothing is
  dropped. The plugin's default is **MusicBee's device, followed**: with nothing chosen the
  output asks MusicBee (`Player_GetOutputDevices`) for its active device at every open, so a
  change in MusicBee's preferences is followed without a restart. MusicBee's "Default" is
  Windows' default.
- **When it applies.** The plugin's output opens the device at every Play from stopped (issue
  06), so the device applies there, with no restart; the mode still waits for MusicBee's next
  start, as issue 04 decided. The dialog's note says both. The engine's rate stays the launch
  device's (ADR 0008); a device at another rate is converted to, as a reopened one is.
- **A device that is not there** (unplugged, or a name the file no longer matches) falls to the
  default device with a notice, "Output device "X" was not found; using the default device.",
  at each open, and is tried again at the next: a DAC that comes back is used again. The mode
  chain is unchanged and runs on the device resolved.
- **The dialog** gains "Output device": "MusicBee's device (name)" first, then every active
  render device by name, and the saved name marked "(not found)" when it is absent, so a choice
  is never lost by opening the dialog. OK waits for Save as before; a changed device goes to
  `ChangeMachineSettings` with the mode, one engine call.
- **The host line** names the device in use: "library · WASAPI shared at 48000 Hz on Speakers
  (C-Media(R) Audio)". The App's title is unchanged (it passes no device).
- **The output's seam** widens: an `IOutputDevices` (names, find by name or the default, mix
  rate, open a player for a mode) replaces the player factory, so the release tests choose and
  lose devices as they faked players.

**The carried items, decided:**

- **The notice at every start in exclusive: fixed.** An output that lets go while stopped no
  longer opens the device at launch at all: it asks the device for its mix rate (WaveOut:
  44.1 kHz) and opens nothing until Play. No stream, no fallback, no untrue notice; `Mode`
  reads the mode asked for until a Play says otherwise. A device that cannot be asked is
  reported as nothing opened, as before.
- **Play while MusicBee's own player plays: the retry.** With `releaseWhileStopped`, the mode
  asked for is retried for up to 300 ms (every 50 ms) before the chain falls back: MusicBee's
  BASS lets go of the device a moment after `Player_Stop` returns. The cause is not proven;
  the Sounds half says whether the notice is gone.
- **The mode after a device loss: fixed.** An output that lets go while stopped reopens from
  the mode asked for, as its Play does; standalone faderio's `Reopen` keeps opening from the
  mode in use (build issue 11).
- **No mode shown: already fixed** by issue 10's host line, now with the device.
- **The known limit:** nothing to build. Recorded in ADR 0012 and `docs/smoke.md` at the verdict.

## Acceptance criteria

- [x] The listener chooses the plugin's output device, and it is remembered per host.
- [x] Each carried item above is fixed, or recorded as decided against.
- [x] The MusicBee smoke covers the setting, and its exclusive-probe item still passes.

## Comments

- **Resolved 2026-10-01.** Built as specified above, with one finding of the smoke folded in.
  - **Core:** `MachineSettings.OutputDevice`, normalised (blank is none); the checked-in
    `machine.json` fixture carries the key. The App's settings form keeps the value it opened
    on; standalone faderio still opens the default device.
  - **The shell:** `IOutputDevices` and `OutputDevice` (internal), `OutputDevices.Installed`
    (NAudio: endpoints by friendly name, a WaveOut player by the waveOut device whose product
    name begins the endpoint's, else the mapper). `NAudioAudioOutput` takes `device` (a func,
    asked at each open) and a devices seam in place of the player factory; `Device` names the
    one in use; `Presenter.FmtHost` takes it. With `releaseWhileStopped`: nothing opened at
    launch (the rate asked for, `Mode` the mode asked for), the first mode retried for 300 ms
    every 50 ms at each open, `Reopen` from the mode asked for.
  - **The plugin:** `Host` resolves the device at each open from the engine's machine settings
    (read and forgotten), else MusicBee's active device *when it names one of this machine's*;
    `SettingsDialog` lists "MusicBee's device (name)", the devices, and a saved one that is
    absent as "(not found)"; `SaveSettings` sends a changed device with the mode; the view's
    host line ends "on *device*". `Plugin.MusicBeesDevice` asks `Player_GetOutputDevices`.
  - **Found in the smoke:** the portable MusicBee runs DirectSound, whose default device is
    named "Primary Sound Driver", which no endpoint matches, so the first build showed "Output
    device "Primary Sound Driver" was not found" at every start. Now MusicBee's name is matched
    against the installed devices in `Host`, and one that is not there (that name, "Default",
    an ASIO driver) is Windows' default with no notice; the notice is kept for a device the
    listener chose. The test covers all three.
  - **Tests:** Core `TheOutputDevice_RoundTrips_AndABlankOneIsNone`; the Presenter's device
    line; `ReleasingTheDeviceTests` on fake devices (launch opens nothing in exclusive with the
    device held, WaveOut at 44.1 kHz without asking, no device at all; the retry opening the
    mode asked for after three refusals, and falling back once spent; reopen after a loss from
    the mode asked for; the device chosen opened and named, changed at the next Play, absent
    then back); `PluginTests` (MusicBee's device followed, "Primary Sound Driver" and ASIO as
    the default; a chosen device saved at Save, asked of the output, in the strip, read back by
    the next start; a saved device not there listed as not found and kept; MusicBee's device
    chosen again saves none). Checked by mutation (each red, then restored): the retry, the
    reopen mode, the save comparison.
  - **The MusicBee Looks half** on the portable MusicBee (DirectSound, "Primary Sound Driver"),
    default device C-Media at 48 kHz, the view in the right sidebar:
    - ✅ **Exclusive at start:** no box, the strip "Ready.", the host line "library · WASAPI
      exclusive at 48000 Hz on Speakers (C-Media(R) Audio)". The probe: opened before any
      play, refused ("Not implemented") while ▶ played, opened after ⏹.
    - ✅ **The dialog** (shared): "MusicBee's device" first (MusicBee's "Primary Sound Driver"
      is none of this machine's), then Speakers (Steam Streaming Microphone), Speakers (Steam
      Streaming Speakers), AI Noise-cancelling Output (ASUS Utility), Speakers (C-Media(R)
      Audio); the note "The mode applies when MusicBee next starts; the device when faderio
      next plays from stopped."
    - ✅ **Another device:** the Steam speakers, OK: the file unchanged, Configure… again on
      it. Save: `"outputDevice": "Speakers (Steam Streaming Speakers)"` in the file, the strip
      "Saved machine settings." with 1 notice and no box, the host line still on the C-Media.
      ▶: the Steam device's session meter 0.300, the C-Media's 0, the host line "… on Speakers
      (Steam Streaming Speakers)". ⏹: both 0.
    - ✅ **A device not there** ("Gone DAC" by hand, restarted): a box owned by MusicBee and the
      strip "Output device "Gone DAC" was not found; using the default device." with 1 notice,
      the host line on the C-Media; Configure… lists "Gone DAC (not found)", selected; Cancel
      and Close keep the file. ▶ plays on the C-Media (0.300) and the strip shows the notice
      ×2; ⏹; the probe opens.
    - ✅ Restored to shared with no device: "Ready." at the next start, the file as found.
    - The launch notice is a box because the view is not yet attached at `Initialise` (issue
      10's rule); the strip shows it too once the panel is filled.
    - ⬜ The Sounds half is the user's: ▶ over MusicBee's own player in exclusive (the retry),
      and a chosen device heard.
  - **Review (2026-10-01).** Standards: no hard breach. Taken: doc comments on
    `OutputDevice`'s members and the retry step; `RateOf` shared by the launch query and the
    open; the dialog keeps a list of the names its items stand for instead of parsing "(not
    found)" back; `_playerDevice` named for what it holds; **Output device** in `CONTEXT.md`.
    Left, recorded: the four branches on `releaseWhileStopped` in the output (a guest-output
    policy object could gather them, with the Stop branch issue 06 left); the output
    factory's signature threading through `Plugin`, `Host` and the tests; `Host` matching
    MusicBee's name against the device list while the output decides what an absent name
    means (two sites, two meanings: the output's is the listener's choice, the host's is
    MusicBee's). Spec: everything asked for is present. Judgement calls, accepted as built:
    the retry tries the mode asked for on any refusal, so a device that never opens
    exclusive costs each Play 300 ms before its fallback notice (the DragonFly answers a held
    device with E_NOTIMPL, so the error code cannot tell the two apart); the dialog reads a
    bare "MusicBee's device" when MusicBee's name is not one of this machine's; a saved device
    that is absent is noticed at launch (the rate query resolves it) and again at Play; a
    machine with no device at all gets one notice and no chain, in both hosts; `Device` names
    the device resolved even when no mode then opened. MusicBee on WASAPI output was not
    smoked: its names are expected to be the endpoints' own, as BASS reports them.
- **The Sounds half, 2026-10-01, the user listening and the agent measuring.** ▶ over
  MusicBee's own player in exclusive: heard MusicBee stop and faderio play, but with "WASAPI
  exclusive unavailable (Not implemented); trying WASAPI shared." in the strip, three rounds
  out of three. The retry did not help, and measuring showed why:
  - MusicBee's output is WASAPI (Shared) with "Primary Sound Driver" (not DirectSound, as the
    Looks half had assumed from the name).
  - From outside MusicBee's process an exclusive open succeeds even while MusicBee plays.
  - Inside it, faderio's exclusive open was refused 0, 0.5, 1, 2, 4, 8, 20 and 45 s after
    MusicBee's player was stopped by a media command.
  - After one exclusive open from another process, the next ▶ opened exclusive.
  So BASS keeps its shared stream initialised in MusicBee's process after a stop, and a WASAPI
  exclusive open from the same process is refused for as long as it exists. The 300 ms retry
  addressed a release delay that does not exist; the user chose to remove it, so a refused
  mode falls back at once again (`Play_FallsBackAtOnce_WhenTheModeAskedForIsRefused` replaces
  the two retry tests). Recorded in ADR 0012's known limits and the smoke's Sounds item: in
  exclusive, faderio opens exclusive until MusicBee's own player is first used in the session.
  Shared, the default, is unaffected. The chosen device and the manager's preview are still to
  be heard.
