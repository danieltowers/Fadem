# 08: Core's whole suite on net48

Status: resolved
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

- [x] Every Core test runs on net48 x86, or is excluded by project item with the reason beside it.
- [x] No net48 test is red or skipped.
- [x] The commit states net8.0 and net48 x86 counts.

## Comments

- **Resolved 2026-09-30.** The net48 target now compiles everything and removes three files
  with the reason beside them: `ChildProcess.cs`, `FlacFrameTableTests.cs` and
  `TruncatedFlacTests.cs`, 50 tests that run a decoder in a child started through `dotnet`.
  Nothing is skipped. 806 tests run on net48 x86 against 855 on net8.0 (855 − 50 + the one
  net48-only `PluginRuntimeTests`).
  - Seven call sites read the same on both targets now (`TestCompat.GetRelativePath`,
    `RuntimeInformation` for the OS check, `CodePages.SystemAnsi()`, a `Process` id, and
    plain overloads of `Split`, `IndexOf`), and a `Compat/Net48Polyfills.cs` in the test
    project supplies `SupportedOSPlatformAttribute`. Core's own stand-ins reach the tests
    through `InternalsVisibleTo`.
  - **Two runtime differences found, one the engine's.** `Path.GetFileName` throws on net48
    for a string that cannot be a path, and the add report's summary called it on a rejected
    M3U entry: `Compat.FileName` now takes the name as net8.0 does, and the existing test
    `AnEntryThatIsNotAPath_IsARejection_AndTheOthersLoad` is the permanent proof, red on
    net48 before. The other was the manager test's own `.Order()` of paths, where "-"
    against "." sorts differently under ICU and NLS: ordinal now.
- **Counts:** Core net8.0 855, Core net48 x86 806, App 274, MusicBee net48 x86 24; 0 failed.
