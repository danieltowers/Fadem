# 06: The plugin lets go of the device while faderio is stopped

Status: ready-for-agent
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
- [ ] With the plugin loaded and faderio stopped, never played or after Stop, another client
      can open the default device in WASAPI exclusive. That applies with the plugin's
      `outputMode` set to each of the three modes.
  - This is checked in the MusicBee Looks smoke by a probe that initialises an exclusive
    `WasapiOut` on the default device and closes it again. It is not left to listening.
- [ ] Play after Stop still plays: at the engine's rate, through the same fallback chain, with
      a notice when no mode opens.
- [ ] Stop from the render thread (the queue running out) lets go of the device without
      deadlocking, and a preview's Stop then Play keeps its player. A test covers each, on a
      fake output that closes the way WASAPI does (as `StoppingTheDeviceTests` does).
- [ ] With A: standalone faderio's Looks smoke items on the output (the title's mode and the
      fallback chain) still pass. With B: all of standalone faderio's Looks half passes, and its
      Sounds items on preview and exclusive are handed to the user.
- [ ] The commit states net8.0 and net48 x86 counts.

## Comments

- **Decided 2026-09-28 by the user: A, the plugin only.** `NAudioAudioOutput` gains a
  constructor option to let go of the device while stopped, and the plugin's `Host` sets it.
  Standalone faderio keeps the device between plays exactly as now. ADR 0012 is unchanged. The
  acceptance criteria's "With A" branch applies.
