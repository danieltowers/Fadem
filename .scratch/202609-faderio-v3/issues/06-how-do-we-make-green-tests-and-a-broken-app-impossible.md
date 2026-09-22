# How do we make "green tests, broken app" impossible?

Type: grilling
Status: resolved
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

**Six seams, not one. A green suite then means something at every layer the user touches,
and the two holes v2 proved — unrepresentative input and an unreachable shell — are closed by
construction rather than by care.**

v2's one seam was good. `TestEngine` wired a *real* store on a temp file and the *real*
decoder factory; only the output was faked. The failure was that it was alone, and everything
the user experienced sat on the far side of it.

### The seams

| | Seam | What a green result means | Catches |
|---|---|---|---|
| **S1** | `IAudioOutput` via `FakeAudioOutput` | engine behaviour is right | envelope, scheduling, transport (as v2) |
| **S2** | `ITrackSource` through the production decoder, **real files** | the decode chain converts correctly | defects A and B, the buzzing, FLAC's dropped seek |
| **S3** | the engine's public API, end to end | *author a point, render, the fade starts there* | the ticket-03 class: late capture, wrong playhead |
| **S4** | the presenter, no WinForms | the shell's remaining logic is right | parse/format, row→adjacency, error summaries |
| **S5** | the Form, headless | controls are wired and configured | the 11-position seek bar, a dead button |
| **S6** | the human: rendered file + smoke checklist | it looks and sounds right | layout, and the ear |

Each defect v2 shipped is invisible to at least four of these. None is invisible to all six.

### S2 — the real-input matrix

**Cells.** Format × rate × depth × channels. For the slice: WAV and FLAC, at 16/24-bit,
44.1/48/96 kHz, mono/stereo. Shaped so that a format [ticket 08](08-what-audio-does-faderio-promise-to-play-correctly.md)
adds later is one row, not a rewrite.

**Three assertions per cell**, all through the production chain, all three on every cell:

1. **Completeness** — rendered frame count equals the decoded length. v2 asserted this
   *nowhere*: `RenderedAudio.Frames` exists and appears in zero assertions, and
   `FramesRendered` is checked only to confirm a paused device rendered nothing. This one
   line would have caught a 48 kHz track stopping 15 % early. It runs on the synthetic
   fixtures too.
2. **Purity** — ticket 01's method, ≥ 0.999 of spectral energy in band. Catches silence and
   the buzzing.
3. **Seek round-trip** — ticket 02's method: pink noise, seek to a known offset, recover the
   landing point by cross-correlation.

The combination matters. Completeness misses silence, purity misses truncation, and neither
catches a seek that lands in the wrong place.

**Fixtures.** WAV is generated at test time by `WaveFileWriter` — any rate, any depth, nothing
checked in. **FLAC is checked in**, generated once by a committed ffmpeg script kept beside the
files. A few MB. Not "generate if ffmpeg is present, skip otherwise": a cell that skips is green
while asserting nothing, and a fresh clone would silently lose the whole FLAC matrix.

**FLAC seek is a pinned characterisation test**: ≤ 40 ms (31 measured plus margin), named for
what it is — `Flac_SeekLandsWithin40ms_PendingTicket09`, never `Flac_SeekIsAccurate`. A known
gap, documented, that catches a regression to 2267 ms. When
[ticket 09](09-how-exact-is-the-authoring-promise.md) sets the real number, this test is the
one place that changes.

**The harnesses are rebuilt, not promoted.** Neither ticket 01's FFT harness nor ticket 02's
cross-correlation harness exists as code — no branch, no script, no dangling commit. Only the
write-ups survive. The methods are fully documented there; the matrix is their first
permanent home.

### S3 — capture is an engine operation

`engine.Capture()` returns a typed **point** from the attended voice's playhead. The shell
displays what it gets. This makes "captured equals heard" an assertion in Core against the
fake output's emitted-frame count, with no shell involved — and it lets the null test drive
capture *itself*: advance the fake to a known frame, `Capture()`, author, render, find the
onset within 5 ms.

There is a correctness reason beyond testability. v2's shell formatted the captured position
with `"0.###"` — **quantising every capture to 1 ms before it was saved**. With a typed point
in frames, the textbox becomes a display of the value, not its source of truth.

S3 tests run against `FakeAudioOutput` in memory. One further test renders the same boundary
through [ticket 11](11-render-a-boundary-to-a-file.md)'s file output and asserts the file
matches the in-memory blocks, so the artifact the human hears is the artifact the suite
measured.

### S4 and S5 — the shell is reachable

**The presenter** holds what's left after ticket 03 and Q3 empty the shell of position logic:
seconds parse/format, queue row → adjacency (with the bounds check that v2's
`EditTransitionAt` lacks), add-files error summary (v2 gave no summary when *every* file
failed), and the store open/recovery path (v2's `OpenTransitionStore` was already decoupled via
an injected `Action<string>` but was `private static` inside the `WinExe`, unreachable).

**Headless-form tests** instantiate the real `MainForm` on an STA thread, never `Show()` it,
and poke controls: `_capture.PerformClick()` then assert the field; assert the seek bar's
range. Milliseconds, no display. Cost: a second test project on `net8.0-windows`, an
`InternalsVisibleTo`, an STA runner. A morning.

**By construction, where possible.** The seek bar's `Maximum` is the track length in
milliseconds — there is no scaling and an 11-position bar cannot be written. Handed to
[ticket 04](04-what-is-the-seam-between-the-shell-and-the-engine.md).

**FlaUI is out.** Slow, flaky on a dev box, and the only thing it adds over S5 is pixels, which
S6 covers for a two-track slice.

### S6 — the human

**The smoke checklist lives at [`docs/smoke.md`](../../../docs/smoke.md)** and runs in every
session that touches the shell, before that session's commit. It is split in two:

- **Looks** — layout and control state. The agent runs it, via the `run` skill's screenshot,
  and reports.
- **Sounds** — the audible items. The user runs it, against the app and the rendered file.

A session never reports "smoke passed" on items it could not perceive.

**The render is one click from authoring.** A single "Render this boundary" button in the
transition editor, writing to a chosen path. One button, no options — ticket 05 kept preview
out, and this must not become preview by another name.

### Enforcement

`dotnet test` before every commit, **and the result reported in the commit message** — written
into `CLAUDE.md`, not a hook. The matrix renders real audio and will take tens of seconds; a
hook that slow gets bypassed with `--no-verify` within a week, and a bypassed hook is worse
than none because it looks like enforcement. The honest control for a solo repo is that the
agent runs the suite and says so where you can check.

**Research leaves a permanent test.** Written into the map's Notes. A harness that renders
through the production chain and measures something *is* an S2 or S3 test; the only difference
is whether it was committed. Tickets 01 and 02 are the proof.

### Defect classes recorded as test targets

From the shell survey, so the rewrite's tests aim at what actually went wrong:

- Seek bar `Minimum`/`Maximum`/`TickFrequency` never set → 11 positions, 24 s granularity on a
  four-minute track, hidden by `TickStyle.None`. (S5, or by construction.)
- Capture quantised to 1 ms by display formatting. (S3.)
- `ParseSeconds` returns null for `""`, `"abc"` and `"-5"` alike — silent failure. (S4.)
- Save/Load conflate an explicit equal-power curve with "unspecified". (S4; curve is out of
  the slice but the pattern recurs.)
- Adding only unreadable files produces per-file notices but no summary. (S4.)
- `SafeInvoke` drops engine events that arrive before the window handle exists. (S5.)
- Store recovery writes to `path + ".recovered"` and never reads it back next launch. (S4.)
- `UnitTest1.cs`: an empty test with no body, passing by doing nothing, survived to the end.
  Delete on migration. Also 30 `.trx` run artefacts checked in.

### Facts this rests on

- v2 fixtures: one writer, `WaveFormat(sampleRate, 16, 2)`, no call site passes a rate. 100 %
  16-bit 44.1 kHz dual-mono WAV. Signals: DC, 1 kHz/2 kHz sine, one ramp. Durations 1–5 s.
- Fade tests assert onset frame and envelope value at hand-chosen indices, precision 2 dp.
  Crossfade tests assert Goertzel gains with ±15 % tolerance. Nothing asserts length.
- `Faderio.App`: `WinExe`, `net8.0-windows`, no `InternalsVisibleTo`, no presenter, no
  interface over the view. Test project is `net8.0`, references Core only.

### Surfaced, not settled here

- **Seek bar in milliseconds by construction** → ticket 04.
- **1 ms capture quantisation** and the **40 ms FLAC pin** → ticket 09.
- **Second test project, `InternalsVisibleTo`, deleting `UnitTest1.cs` and the `.trx`
  files** → ticket 07.
- **Matrix shape** (a format is one row) → ticket 08.

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
