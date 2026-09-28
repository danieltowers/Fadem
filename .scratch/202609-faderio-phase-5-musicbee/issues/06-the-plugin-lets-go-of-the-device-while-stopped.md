# 06: The plugin lets go of the device while faderio is stopped

Status: resolved
Blocked by: 03

Part of the [MusicBee spike](../spec.md). Found while building [issue 03](03-the-plugin-plays-the-open-playlist-inside-musicbee.md):
the plugin holds the audio device from MusicBee's start-up until MusicBee closes, whether or
not faderio ever plays.

## The problem

The spec's Solution says the plugin "lets go of the device when stopped, in WASAPI shared and
exclusive mode". User stories 15 and 21 say the same:

- **15:** Stop releases the device, so MusicBee's own player can play straight away, even in
  exclusive mode.
- **21:** MusicBee's player is untouched while faderio is stopped.

Neither holds today. The output does what standalone faderio needs, not what a guest in
another player's process needs.

- **At construction.** `NAudioAudioOutput` opens the device when it is built
  (`OpenFirstAvailable` → `Open` → `player.Init`). The plugin builds it in `Initialise`, at
  MusicBee's start-up.
  - It opens at construction on purpose. The engine's rate is the device's, fixed at launch
    (ADR 0008).
  - In WASAPI exclusive, `WasapiOut.Init` initialises an exclusive audio client. That takes the
    endpoint from every other client, MusicBee's player included.
- **On Stop.** `NAudioAudioOutput.Stop` calls the player's `Stop`, which stops the stream but
  keeps the audio client initialised. The device is still held.
- **Only `Dispose` lets go.** The engine disposes the output on `Close` or `Uninstall`, that is,
  when MusicBee closes or the plugin is switched off.

So with the plugin's `outputMode` set to `WasapiExclusive`, MusicBee's own player cannot use the
device at all while MusicBee runs. The Sounds item "MusicBee's own player plays again straight
after faderio's Stop, in WASAPI exclusive" (issue 05) must fail.

**Not known yet:** whether faderio's idle *shared* stream blocks MusicBee. It would if MusicBee
opens the device exclusively itself. The portable MusicBee has `<PlayerOutputApi>1</PlayerOutputApi>`,
and it is not known which API that is. In issue 03's smoke, MusicBee's own player could not be
started under automation at all, with or without the plugin. That one try was inconclusive.

## What any fix must keep

- **The engine's rate is fixed at launch** (ADR 0008). An output that lets go while stopped
  reopens at the engine's rate and converts, as `Reopen` already does after device loss. It never
  changes the rate.
- **Stop can come from the render thread.** When the queue runs out, the engine stops the
  output from inside a render. `CountingSource.RequestStop` then ends the stream, and the player
  stops itself. The device can only be let go after its `PlaybackStopped`, never from the
  render thread.
- **Stop then Play in one command** (a preview starting, build issue 16). A `PlaybackStopped`
  posted after a Play that followed the Stop must not close the player that Play opened.
- **A failed reopen at Play** is a notice, and playback stays stopped. Today's lost-device path
  already does this (`Stopped(exception)`, then the next Play reopens with a notice). The
  fallback chain and its notices (exclusive → shared → WaveOut) apply as at launch.
- **The placement tests.** The plugin's Stop stays one engine call. Letting go of the device is
  the output's business, not the shell's.

## The decision this needs

**Which host lets go of the device while stopped?**

- **A. The plugin only (recommended).** The output takes a constructor option, such as "release
  while stopped", and the plugin sets it.
  - Standalone faderio behaves exactly as now: no reopen latency before each play or preview,
    and its smoke and Sounds items are unaffected.
  - The cost: the two hosts' outputs differ in one respect. User story 33 asks for them to
    behave identically.
- **B. Both hosts.** Standalone faderio also lets go when stopped.
  - One behaviour, and exclusive mode stops blocking other apps while faderio is idle.
  - The cost: every Play and every preview start reopens the device (tens of ms, more in
    exclusive), and standalone faderio's Sounds half needs rerunning.
- Rejected:
  - **Building the output only when Play is chosen.** The engine is built on its output, so the
    plugin would rebuild the engine each time. The open playlist then reloads in the
    background, and Play would find it empty.
  - **Not offering exclusive in the plugin.** That contradicts brief decision 8, and still
    leaves the shared question open.

## Acceptance criteria

- [x] The decision above is recorded here, and in ADR 0012's Consequences if it is B.
- [x] With the plugin loaded and faderio stopped, never played or after Stop, another client
      can open the default device in WASAPI exclusive. That applies with the plugin's
      `outputMode` set to each of the three modes.
  - This is checked in the MusicBee Looks smoke by a probe that initialises an exclusive
    `WasapiOut` on the default device and closes it again. It is not left to listening.
- [x] Play after Stop still plays: at the engine's rate, through the same fallback chain, with
      a notice when no mode opens.
- [x] Stop from the render thread (the queue running out) lets go of the device without
      deadlocking, and a preview's Stop then Play keeps its player. A test covers each, on a
      fake output that closes the way WASAPI does (as `StoppingTheDeviceTests` does).
- [x] With A: standalone faderio's Looks smoke items on the output (the title's mode and the
      fallback chain) still pass. With B: all of standalone faderio's Looks half passes, and its
      Sounds items on preview and exclusive are handed to the user.
- [x] The commit states net8.0 and net48 x86 counts.

## Comments

- **Decided 2026-09-28 by the user: A, the plugin only.** `NAudioAudioOutput` gains a
  constructor option to let go of the device while stopped, and the plugin's `Host` sets it.
  Standalone faderio keeps the device between plays exactly as now. ADR 0012 is unchanged. The
  acceptance criteria's "With A" branch applies.
- Built 2026-09-28, under decision A.
- **The output.** `NAudioAudioOutput` gains `releaseWhileStopped` (constructor, off by default).
  With it:
  - The player opened at launch is closed once the rate is known. `Initialise` now tells the
    engine "nothing opened at launch" from `Mode` being null, not from the player being null.
  - **Stop** closes the player after the player's own Stop. A stop from the render thread (the
    queue running out) closes it on the player's `PlaybackStopped`, which NAudio posts to the
    UI thread where there is one and raises on the render thread otherwise; both are safe,
    since the player has already stopped itself by then and its Dispose joins nothing.
  - **Play** opens the device again from the mode *asked for*, not the mode in use, through
    the same chain at the engine's rate: a device MusicBee's player held at launch is tried
    for exclusive again. `Reopen` is unchanged (from the mode in use, as before).
  - **No mode at Play** is reported as a player that started and then failed: the output says
    it is playing, and posts `Stopped(exception)` off the call (to the UI thread, or a pool
    thread). The engine then parks where the listener was with "Audio output lost (no audio
    output could be opened)…", and its next Play goes through `Reopen` and its own "No audio
    output device to play on…" notice. Raising it inside Play would run the park inside the
    Play command.
  - A posted clean stop closes the player only while the output is still stopped: after a Play
    that restarted the same player, or opened another, nothing is closed. A player is closed
    once, by whichever thread gets there first (`Interlocked.Exchange`).
  - The plugin's `Host` passes `releaseWhileStopped: true`; standalone `Program` passes nothing.
- **Seam for tests:** an internal constructor takes a player factory,
  `Func<AudioOutputMode, IWavePlayer>`; `Faderio.Shell` is `InternalsVisibleTo` the plugin's
  tests. Tests use WaveOut, whose chain never asks the real device for its mix rate.
- **Tests,** `ReleasingTheDeviceTests` (net48 x86, 8): the real output and the real engine on a
  fake NAudio player whose Stop joins its render thread, whose read of nothing ends the stream
  from that thread, and whose stop is posted to the synchronisation context captured when it
  was made or raised on the render thread, as NAudio's are. Let go at launch; Stop lets go and
  Play opens again at the same rate; the queue running out lets go from the render thread
  within patience, and when posted to a UI thread, once the test drains it; a preview from a
  paused queue (Stop then Play in one command) keeps the player it opened when the old stop
  arrives after; Play before a render-thread stop is delivered keeps its player; no mode at
  Play gives the output's fallback notice, then the engine's "Audio output lost", paused,
  then "No audio output device" at the next Play, then a free device plays; without the option
  the device is kept between plays.
  - A preview starting while *playing* never stops the device: the queue is held and the
    player plays on. The Stop-then-Play case is a preview from a paused queue.
- **Checked by mutation** (each red, then restored): Stop not closing; the render-thread stop
  not closing; the handler closing while playing; launch keeping the player; a failed open
  not reported.
- **Counts:** Core net8.0 853, Core net48 x86 288, App 274, MusicBee net48 x86 24; 0 failed.
- **Standalone Looks, the output items:** `--unavailable=exclusive` with exclusive chosen:
  title "WASAPI shared at 48000 Hz", strip "WASAPI exclusive unavailable (the device is in use
  (--unavailable)); trying WASAPI shared." ✅ `--unavailable=exclusive,shared`: "WaveOut at
  44100 Hz", strip "WASAPI shared unavailable …; trying WaveOut." ✅ Exclusive chosen with no
  flag: "WASAPI exclusive at 48000 Hz" ✅.
- **MusicBee Looks, in the portable MusicBee.** Smoke library authored through Core at
  `faderio-smoke\library` (and `library2`): two 10 s
  tones at 0.3 (440 Hz, 660 Hz), a 2 s crossfade at 6 s.
  - ✅ Installed, MusicBee starts with no error box; `MusicBee3Settings.ini` lists
    `mb_faderio.dll` "faderio", enabled, after each close.
  - ✅ **Tools → faderio** reads **Play**, **Stop**. faderio is now second from last in Tools:
    "Skip to Near End" follows it.
  - ✅ **Play** puts MusicBee's session at 0.300, **Stop** at 0.000, in WASAPI shared and in
    WaveOut. In exclusive the session meter, and this device's own meter, show nothing: an
    exclusive stream bypasses them. That exclusive was playing is proven by the probe below.
  - ✅ **The device is free while stopped**, by the exclusive probe:
    - shared: opened before any play, after Play then Stop, and after the queue ran out on its
      own (the posted stop on MusicBee's UI thread);
    - WaveOut: opened before and after;
    - exclusive: opened before any play; **refused while faderio played** ("Not implemented",
      see below), so Play had opened exclusive; opened after Stop, on two Play/Stop rounds.
  - ✅ **Configure…** opens faderio's dialog on the file's library and its mode by name, with
    "Applies when MusicBee next starts."
  - ✅ `library2` and WaveOut, OK: the file unchanged; Configure again opens on the pending
    choice; **Save** writes both, shows "Saved machine settings.", `library2` has its
    `.faderio`; after a restart the dialog opens on them.
  - ✅ `C:\gone\library`, saved: "The library library could not be opened (the folder does not
    exist). Nothing is saved until a library opens; what is queued still plays."; the file
    keeps `library2` (the mode, changed too, was saved). `music\library`: "\"music\library\"
    is not a folder's full path." and the dialog stays; Cancel, then Close, change nothing.
  - ✅ With `Plugins\faderio\` removed: "faderio could not start: Could not load file or
    assembly 'Faderio.Core, Version=1.0.0.0…'. The system cannot find the file specified.",
    MusicBee runs, the plugin stays enabled. Folder put back, settings restored to the smoke
    library in WASAPI shared.
- **Found in the smoke, for the verdict (05):**
  - **At MusicBee's start with exclusive chosen**, the launch open fails: "WASAPI exclusive
    unavailable (Not implemented); trying WASAPI shared." Something holds the device
    exclusively at that moment (MusicBee's own player initialising, most likely), and this
    device, an AudioQuest DragonFly Cobalt, answers a second exclusive client with E_NOTIMPL
    rather than the usual in-use code: the probe gets the same "Not implemented" while faderio
    plays exclusive. Play opens exclusive anyway, because Play retries from the mode asked.
    So exclusive works, with a notice at every start that is not true by the time Play is
    pressed, and no title in the plugin to say which mode is in use.
  - **An exclusive probe while faderio plays shared evicts it:** "Audio output lost (Exception
    from HRESULT: 0x88890004). Playback is paused; press Play to resume.", and the next Play
    reopened and played. The lost-device path works in MusicBee; the smoke doc now says never
    to probe mid-play.
  - **Every crossfade shows** "\"tone-a-440.wav\" ends 2.0 s before its header claims.": the
    engine's early-end notice misfiring on an authored fade-out. Reproduced headless, filed as
    [issue 07](07-an-authored-fade-out-is-reported-as-the-track-ending-early.md).
  - MusicBee's own "The source file for track '…' could not be found" appeared
    once: its player, started by a stray key in the menu walk on a file its library has lost.
