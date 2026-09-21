# What exactly is in the narrow slice?

Type: grilling
Status: open
Blocked by: 01

## Question

The user chose a narrow vertical slice over restaging all 14 of v2's Phase 1 issues, because
the thing that killed v2 is that nothing was provable until everything was written.

The slice is roughly: **load two tracks, author the boundary between them by ear, hear the
fade land where you put it.** That sentence needs turning into a boundary sharp enough to
build to and to stop at.

To settle:

1. What is the minimum UI? A queue that holds two tracks, transport, a real seek bar, the
   transition editor for one boundary. Is a visualiser in or out? It is the fastest way to
   *see* a fade happening, which argues for in; it is also pure surface area, which argues
   for out.
2. Which of the four transition parameters are authorable in the slice? All four, or just the
   fade-out point and fade-out duration, with the incoming side following once the outgoing
   side is trustworthy?
3. Does the slice persist anything? Reusing v2's JSON store costs almost nothing and is
   already tested, but an in-memory transition would prove the audio loop sooner.
4. Which audio formats must work in the slice? The honest answer depends on ticket 01 and
   ticket 02: if compressed seeking is inaccurate, the slice may need to be WAV-only to be
   provable at all, with formats as the next ticket rather than a hidden assumption.
5. What is explicitly deferred, written down so it does not creep back in: repeat, shuffle,
   preview, ReplayGain, settings dialog, M3U, multi-track queues, end-of-queue behaviour.
6. **What is the acceptance test the user personally performs?** The slice is done when the
   user does a specific thing and hears a specific result. Write that sentence down.

## Answer

_(pending)_
