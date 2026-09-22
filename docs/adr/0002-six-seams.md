# The suite tests at six seams, with real files checked in and the Form instantiated headless

Status: accepted

v2 had one test seam — a fake `IAudioOutput` behind a real store and a real decoder — and it was
a good one. Eighty-seven tests passed while the app was unusable, because every defect the user
hit lived on the far side of that seam, every fixture was 16-bit 44.1 kHz WAV so the decode
chain never converted anything, and nothing asserted that a track rendered to its full length.
We test at six seams instead: the fake output (kept), real files through the production decoder,
the engine end to end, a presenter, the real Form instantiated headless, and the human with a
rendered file and a smoke checklist.

Decided in [ticket 06](../../.scratch/202609-faderio-v3/issues/06-how-do-we-make-green-tests-and-a-broken-app-impossible.md),
which holds the detail. The smoke checklist is [`docs/smoke.md`](../smoke.md).

## Considered options

**A thinner shell instead of a tested one.** Rejected: v2's shell *was* thin — 1033 lines,
decomposed by concern — and still shipped every defect the user hit. Thinness is not the
property; reachability by a test is. So the shell gets a second test project on
`net8.0-windows` with an `InternalsVisibleTo`, and the real `MainForm` is constructed on an STA
thread, never shown, and its controls poked directly. A future reader will find a test project
referencing a `WinExe` and wonder; this is why.

**Driving the real window with UI Automation (FlaUI).** Rejected: slow, flaky on a dev box, and
the only thing it adds over the headless Form is pixels, which the smoke checklist covers for a
two-track slice.

**Generating compressed fixtures at test time if ffmpeg is present, skipping otherwise.**
Rejected: a skipped cell is green while asserting nothing, and a fresh clone would silently lose
the whole FLAC matrix. FLAC fixtures are **checked in**, a few MB, with the generating script
beside them. Do not "tidy" them out of the repo.

**Leaving FLAC's known 31 ms seek error red, or unasserted, until fixed.** Rejected both ways: a
permanently red test teaches everyone to ignore red, and an absent one is a silent gap. It is a
pinned characterisation test at 40 ms, named for the ticket that will tighten it
(`…_PendingTicket09`), so it documents the defect and catches a regression to 2267 ms.

**Enforcing the suite with a pre-commit hook.** Rejected: the real-input matrix renders audio
and takes tens of seconds, and a hook that slow gets bypassed within a week — after which it
looks like enforcement and is not. The rule is `dotnet test` before every commit with the result
stated in the message, so that it can be checked.

## Consequences

Capture is an engine operation returning a typed point, not a shell read of the playhead. This
is what makes "the point I captured equals the point I heard" an assertion in Core — and it
also removes a quantisation v2 had, where the shell formatted the position to milliseconds in a
string before saving it.

Every cell of the real-input matrix asserts three things, and all three are required:
completeness (rendered frames equal decoded length), purity (spectral energy in band), and seek
round-trip. Each defect v2 shipped is invisible to two of the three.

The FFT and cross-correlation harnesses that produced the map's most decision-shaping findings
did not survive as code. They are rebuilt as the matrix's first rows, and research on this
project now leaves a permanent test behind — a harness that measures the production chain is
already an integration test; the only difference is whether it was committed.
