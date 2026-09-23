# When does ReplayGain switch, and is it on by default?

Type: grilling
Status: open
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
