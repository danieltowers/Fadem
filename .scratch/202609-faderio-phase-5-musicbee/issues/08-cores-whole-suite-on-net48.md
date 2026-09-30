# 08: Core's whole suite on net48

Status: needs-triage
Blocked by: 05

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md), ADR 0012). Staged
at the verdict; it needs its own spec before an agent builds it.

## What to build

`Faderio.Core.Tests` compiles its whole suite for net48 x86, not only the promise and the file
formats. Today the net48 target removes every file and includes a named few (see the test
project's net48 item group); 290 tests run there against 855 on net8.0.

Known from the spike:

- Tests built on `ChildProcess` start children through `dotnet`, which a net48 test binary
  cannot use. They need a net48 way to start a child, or stay net8.0-only by project item.
- A test that cannot compile on net48 is excluded by project item, never skipped.
- The commit rule already states both counts.

## Acceptance criteria

- [ ] Every Core test runs on net48 x86, or is excluded by project item with the reason beside it.
- [ ] No net48 test is red or skipped.
- [ ] The commit states net8.0 and net48 x86 counts.
