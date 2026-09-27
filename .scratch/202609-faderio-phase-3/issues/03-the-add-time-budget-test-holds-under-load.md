# 03: The add-time budget test holds under load

Status: ready-for-agent
Blocked by: —

Seen in [playlists 05](../../202609-faderio-phase-2-playlists/issues/05-the-transition-manager.md) and
[playlists 06](../../202609-faderio-phase-2-playlists/issues/06-configurable-columns.md).

## What happens today

`tests/Faderio.Core.Tests/AddTimeBudgetTests.cs` times one `Add` with a `Stopwatch` against a
fixed budget: every fixture within 1000 ms, and a FLAC with 8 MB of cover art within 150 ms. Alone,
both pass with room to spare. In a full `dotnet test`, xunit runs the other test classes
alongside them, and the App test assembly runs at the same time in its own process. Under that
load one of them fails about one run in three (2 of 5 in playlists 06: 166 ms against 150). Both
fail the same way on `HEAD` without any change. Each commit message has to explain the failure
away, and a real regression would look the same.

**Not this issue, but seen beside it.** Several full runs on 2026-09-27 printed no App assembly
result at all. This was first put down to the Core failure, which was wrong: a run with Core
green did the same. The cause was a `faderio.exe` launched from `src/Faderio.App/bin/`, which
locks `Faderio.Core.dll` there. The App project then fails to build (MSB3027), and `dotnet test`
never runs its tests. The only sign is a build error above a summary that lists Core alone.
Close faderio before `dotnet test`, or check that both assemblies report. Running
`dotnet test tests/Faderio.App.Tests --no-build` against the last build is a fallback, not a
test of new code.

The class's own summary says what the budgets are for: an order-of-magnitude regression, such as
a whole-file scan creeping into the probe, not a few milliseconds.

## What to build

- **Best of three.** Each test times its add three times (after the existing warm-up) and holds
  the fastest to the budget. A regression slows every run; load slows only some. The failure
  message reports all three times.
- **Run alone within its assembly.** Put the class in an xunit collection with
  `DisableParallelization = true`, so no other Core test class runs beside it. This cannot stop
  the App assembly's process, which is why best-of-three is the main fix.
- The budgets stay 1000 ms and 150 ms. Raise one only if the change above still fails, and
  record the timings that justify it in this issue.

## Where the logic lives

Tests only. No production code changes.

## Acceptance

- [ ] Ten consecutive full `dotnet test` runs, with no `AddTimeBudgetTests` failure. Record the
      fastest-of-three times seen.
- [ ] A slowdown in every add still fails both tests: a 200 ms sleep put into
      `TrackMetadataReader.Read` makes both fail (checked, then removed).

## Tests

This issue is a test. The mutation above is its check.

## Comments
