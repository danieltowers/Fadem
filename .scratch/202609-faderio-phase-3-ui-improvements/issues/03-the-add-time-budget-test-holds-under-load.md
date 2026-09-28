# 03: The add-time budget test holds under load

Status: resolved
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

- [x] Ten consecutive full `dotnet test` runs, with no `AddTimeBudgetTests` failure. Record the
      fastest-of-three times seen.
- [x] A slowdown in every add still fails both tests: a 200 ms sleep put into
      `TrackMetadataReader.Read` makes both fail (checked, then removed).

## Tests

This issue is a test. The mutation above is its check.

## Comments

- Built 2026-09-27. `AddTimeBudgetTests` now times each add three times after the warm-up and
  holds the fastest to the budget. The failure message lists all three times, and the times also
  go to the test output, which `--logger "console;verbosity=detailed"` shows on a passing run. The
  class is in the collection "Add-time budget", with `DisableParallelization = true`. The fixtures
  add goes into the same engine each time, so the queue grows by 50 rows per run; that is well
  inside the budget. Budgets unchanged: 1000 ms and 150 ms.
- **Ten consecutive full runs**, 2026-09-27, all green: Core 814 passed, App 269 passed each time.
  Built once, then `dotnet test --no-build`, with `--artifacts-path` because faderio was running
  from `src/Faderio.App/bin`. Times are the fastest, then the three runs in order (ms):

  | Run | Every fixture (budget 1000) | 8 MB cover art (budget 150) |
  |---|---|---|
  | 1 | 42 (49, 42, 46) | 28 (81, 28, 57) |
  | 2 | 42 (42, 43, 44) | 33 (**285**, 33, 65) |
  | 3 | 31 (149, 31, 45) | 43 (76, 43, 65) |
  | 4 | 42 (76, 49, 42) | 30 (72, 30, 53) |
  | 5 | 57 (432, 57, 253) | 52 (**438**, 66, 52) |
  | 6 | 47 (84, 47, 49) | 81 (104, 81, 82) |
  | 7 | 36 (94, 36, 40) | 28 (71, 28, 67) |
  | 8 | 57 (488, 62, 57) | 34 (94, 34, 78) |
  | 9 | 38 (405, 38, 49) | 31 (**171**, 31, 59) |
  | 10 | 41 (94, 41, 50) | 27 (77, 27, 59) |

  The first time in each run is the one the old test took. The bold ones are over 150 ms, so the
  old test would have failed runs 2, 5 and 9, which matches the "one run in three" this issue
  began with. The slowest fastest-of-three was 81 ms against 150, and 57 ms against 1000.
- **Mutation:** a `Thread.Sleep(200)` at the top of `TrackMetadataReader.Read` failed both tests:
  every fixture at best 10549 ms (10719, 10549, 10560), and cover art at best 240 ms (402, 254,
  240). Removed; the file is unchanged.
