# Does the visualiser come back?

Type: grilling
Status: open
Blocked by:

## Question

v2 issue 0011 built a bottom strip showing both voices' fade levels live, a peak meter and a
clipping indicator, pitched as authoring feedback. The slice cut it (ticket 05 of the
predecessor map): the render harness is a better instrument for proving where a fade lands,
and ADR 0001 requires the bars to move with the playhead, not the decode position, so it is
no longer free.

On a daily-driver player, does it come back, and as what?

- **Cut** for good.
- **Peak meter and clip light only**, which a listener uses for loudness and ReplayGain.
- **The full strip**, on audible time.

If it returns, say what the engine must publish in the `View` for it (placement test 2: the
shell formats, never computes).
