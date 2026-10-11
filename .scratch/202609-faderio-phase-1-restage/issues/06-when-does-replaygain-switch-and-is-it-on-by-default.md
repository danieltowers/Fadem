# When does ReplayGain switch, and is it on by default?

Type: grilling
Status: resolved
Blocked by:

## Question

v2 issue 0012 reads `REPLAYGAIN_TRACK_GAIN` from tags, applies it in the per-track gain chain,
clamps at ±12 dB, and puts it behind a setting. v3 still reads it through TagLib; the
predecessor found and fixed v2's reader throwing on every ID3v2-tagged file.

Decide:

- **Default.** On or off out of the box, and what a track with no tag gets (0 dB, or a
  pre-amp).
- **Switchover.** Gain is per voice, so during a crossfade each voice carries its own. Confirm
  that, and that a toggle mid-play takes effect at the playhead, not the decode position.
- **Where audible-time discipline stops.** The predecessor put the playhead, seek bar, time
  readout and visualiser on audible time. Does every user-visible signal owe the same: the
  end-of-track notice, queue advance, the ReplayGain switch, the now-playing row? Settle the
  rule once here, for all of them, so tickets 02 and 03 can cite it.
- **Album gain** is out of scope (Phase 2); confirm track gain alone is enough.

## Comments

- From [Does the visualiser come back?](03-does-the-visualiser-come-back.md): clipping is
  indicated by an engine-driven clip light and not prevented. Peak-aware gain
  (`REPLAYGAIN_TRACK_PEAK`) is still this ticket's option to take or leave; the light covers
  boost clipping either way. The visualiser no longer exists, so drop it from the audible-time
  list above and put the clip light in its place.

## Answer

Grilled with the user on 2026-09-23. `CONTEXT.md` gains **Audible time**, and ADR 0001 gains
an addendum stating the rule below.

### The fact that shaped it

The user's library is almost entirely untagged. A sample of 342 files under
the user's music folder (300 of about 2,500 MP3s and 42 of 309 FLACs, every seventh file,
first 256 KB) found **2** with any loudness tag: `REPLAYGAIN` in ASCII or UTF-16, or
`iTunNORM`. ReplayGain as built is inert for about 99% of what is played.

### ReplayGain in Phase 1

- **It stays**, tags only: **track gain, on by default**, with a toggle in the settings
  dialog. The engine work is already done and tested. Tagging the library with an external
  scanner (rsgain, foobar2000) makes it useful.
- **An untagged track plays at 0 dB, with no pre-amp.** A pre-amp would turn down nearly the
  whole library to match two files.
- **No album gain** (v2's spec puts it in Phase 2), and **the peak tag is not read**. The clip
  light from [Does the visualiser come back?](03-does-the-visualiser-come-back.md) covers
  boost clipping, and with an untagged library, peak protection would protect nothing.
- **Loudness scanning is out of scope** for this map: it is a library feature (Phase 2).

### When it switches

- **Gain is per voice**, as the engine already does. During a crossfade, each voice carries
  its own tag's gain.
- **A toggle applies at once to every live voice**, including preview voices, not only from
  the next track. Each voice's base gain ramps to its new value over a few milliseconds so
  there is no click. It is heard one device buffer later, under the rule below. The setting is
  fixed at engine construction today, so the build makes it changeable at runtime.

### The audible-time rule, for the whole map

- **The `View` describes what the listener hears.** The playhead, the attended voice, the
  now-playing row, *Stopped*, the clip light and the preview's phase all follow audible time.
- **Commands take effect at the decode position**, as soon as possible, and are heard one
  device buffer later. The engine never delays a command to line it up with the playhead.
  Seek, skip, Transition now, a ReplayGain toggle and queue advance all work this way.
- **Settings in the `View` show their value as set.** They are not sound, so they are not
  delayed.
- **Notices are delivered when raised** and carry no position.

This is ADR 0001 stated generally: the playhead already stays honest through the buffer after
a seek while the sound catches up. It asks nothing new of
[What do skip, repeat and end-of-queue do?](02-what-do-skip-repeat-and-end-of-queue-do.md) or
[Does the visualiser come back?](03-does-the-visualiser-come-back.md), and both can cite it.

### Consequences for other tickets

[Which settings exist, and where do they live?](10-which-settings-exist-and-where-do-they-live.md)
gets a ReplayGain toggle, on by default, that changes at runtime.
