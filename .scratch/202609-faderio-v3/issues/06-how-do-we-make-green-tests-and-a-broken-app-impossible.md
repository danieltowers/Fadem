# How do we make "green tests, broken app" impossible?

Type: grilling
Status: open
Blocked by: 01

## Question

The defining fact about v2: roughly ninety tests passed, and the user could not use the app.
That combination has to be made impossible rather than merely unlikely, because it is the
failure mode this project is prone to.

It happened because of where the tests were pointed, not because they were bad tests. They
covered `Faderio.Core` thoroughly and through a real deterministic seam. They covered the
WinForms shell not at all, and the shell held every defect the user hit. They also only ever
opened synthetic WAV files, so an entire class of format bug was invisible.

The user has already settled the headline question: **both** automated assertions and an
audible artifact they can check themselves. This ticket designs that.

To settle:

1. **The offline render.** The engine renders a transition to a WAV file without a sound
   device. What drives it: a command-line entry point, a menu item in the app, a test helper,
   or all three? Can the user point it at two files and a transition and get something to
   listen to in one step?
2. **What the automated assertions check that they did not before.** Real compressed formats,
   mono and non-44.1 kHz sources, and the authored point landing where it was authored
   measured end to end rather than per component.
3. **The capture path specifically.** The latency bug in ticket 03 was untestable because
   capture lived in a WinForms event handler. What has to move out of the shell so that
   "the point I captured equals the point I heard" becomes an assertion?
4. **How much of the shell gets tested, and how?** Options: keep the shell so thin that its
   remaining code is inspectable at a glance; extract a presenter holding the logic and test
   that; or drive the real window in an automated UI test. The first two are cheap and the
   third is not.
5. **What catches the defects that tests structurally cannot**, such as an 11-position seek
   bar or a panel whose title renders behind another control. A manual smoke checklist the
   user runs at the end of the slice is probably the honest answer. Write it.
6. Is there a check that would have caught the buzzing before the user heard it? Ticket 01
   built exactly such a harness, rendering offline through the production chain and measuring
   tonal purity with an FFT. It should become a permanent test rather than a one-off.

**Direct evidence from ticket 01, which strengthens this ticket considerably.** The suite did
not merely miss defects in the untested shell. It passed while `Faderio.Core` itself
truncated every file above 44.1 kHz and rendered ordinary 24-bit WAVs as silence. Both live
in code the tests exercise heavily. They survived because every test feeds synthetic 16-bit
44.1 kHz WAV, so the conversion layer was never asked to convert anything.

That reframes question 2 above from a nice-to-have into the core of this ticket: the test
suite needs a matrix of real decoded inputs across rates, bit depths and channel counts, and
it needs assertions on **duration and completeness**, not only on waveform shape. A truncated
track and a correct one look identical to an assertion that only checks the fade envelope.

## Answer

_(pending)_

## Noted from ticket 05

The shell survey sharpens this ticket's premise considerably, and corrects it.

**The shell was never fat.** It is 1033 lines across 9 files, cleanly decomposed: `MainForm`
split into four partials by concern, panels isolated, composition root separate, NAudio behind
a factory. Core is 2642 lines. The map's original "untested ~800-line shell" framing invites the
wrong lesson.

**The shell is untestable, not merely untested.** There is exactly one test project, it
references `Faderio.Core` only, and **no test project references `Faderio.App` at all**. All 87
`[Fact]`/`[Theory]` tests run against Core through the `IAudioOutput` seam with
`FakeAudioOutput`. Nothing was ever run against those 1033 lines, and nothing could be.

So "ninety tests passed and the user could not use the app" is not a paradox and not a coverage
gap — it is the arithmetic working exactly as built. The tests covered Core, Core was mostly
fine, and every defect the user hit lived in the untested region. Any answer to this ticket has
to make that region reachable, or it has not addressed the failure at all.

Two more facts worth having:

- `UnitTest1.cs` is an empty xUnit scaffold whose single test method has **no body** — it passes
  by doing nothing, and it survived to the end of the project.
- 30 `.trx` run artefacts (~10,000 lines) are checked into `tests/.../TestResults/`.

Ticket 05 also put the render harness ([ticket 11](11-render-a-boundary-to-a-file.md)) *before*
the slice precisely so verification is not the last thing to arrive. That is one concrete
mechanism this ticket can build on: an audible artifact producible without launching the UI.
