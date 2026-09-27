# 03: The add-time budget test holds under load

Status: ready-for-agent
Blocked by: —

Seen in [playlists 05](../../202609-faderio-playlists/issues/05-the-transition-manager.md) and
[playlists 06](../../202609-faderio-playlists/issues/06-configurable-columns.md).

## What happens today

`tests/Faderio.Core.Tests/AddTimeBudgetTests.cs` times one `Add` with a `Stopwatch` against a
fixed budget: every fixture within 1000 ms, and a FLAC with 8 MB of cover art within 150 ms. Alone,
both pass with room to spare. In a full `dotnet test`, xunit runs the other test classes
alongside them, and the App test assembly runs at the same time in its own process. Under that
load one of them fails about one run in three (2 of 5 in playlists 06: 166 ms against 150). Both
fail the same way on `HEAD` without any change. Each commit message has to explain the failure
away, and a real regression would look the same.

It costs more than the one test. In each of the three runs where Core failed (playlists 06 and
the commit that opened this folder), the solution-level `dotnet test` output had no App assembly
result at all, not even a build line. The App count then had to come from a second
`dotnet test tests/Faderio.App.Tests` run. Whether the App assembly is skipped or only its
output is lost is not known.

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
- [ ] Find out why a Core failure leaves no App result in the solution-level output (make any
      one Core test fail on purpose and run `dotnet test`). If the App assembly is skipped,
      record the command that runs both regardless, and use it in `CLAUDE.md`'s verification rule.
- [ ] A slowdown in every add still fails both tests: a 200 ms sleep put into
      `TrackMetadataReader.Read` makes both fail (checked, then removed).

## Tests

This issue is a test. The mutation above is its check.

## Comments
