# What happens when a file or the device fails?

Type: grilling
Status: open
Blocked by: 01, 02

## Question

v2 issue 0014: one bad file never stops playback. The slice kept only the narrowest sliver: a
file that fails to open at add time is rejected with a message. Decide the rest:

- **A track fails at play time** (deleted, moved, truncated, a network drive gone after it was
  added). Skip it with a notice? What happens to the adjacency's transition when its incoming
  track cannot open, given ticket 02's skip rules?
- **Audio ends before `Length`.** ADR 0010 says one notice and the track ends there; confirm
  how that interacts with a scheduled fade-out point beyond the real end.
- **Device failure.** WASAPI fails to initialise, or the device disappears mid-play (headphones
  unplugged). v2 fell back to WaveOut and surfaced the downgrade. Which fallbacks, and does the
  fallback change the engine rate (ADR 0008 fixes it at launch)?
- **Where notices live.** The status strip, a log, both; and whether an engine error can ever
  be swallowed (v2 said never).
