# Tests on an optional host are compiled only where it is installed

Status: accepted (2026-10-11)

`MusicBeeBassWmaTests` measure how MusicBee's own BASS seeks WMA
([research](../research/wma-through-musicbee-bass.md)). They load `bass.dll` and `basswma.dll`
from a MusicBee install, which the repo does not ship and may not redistribute. Until now a
machine without MusicBee failed them, so a stranger's first `dotnet test` was red.

**The project compiles them only where MusicBee's `bass.dll` is.** `Fadem.Core.Tests.csproj`
looks in `FADEM_MUSICBEE_DIR`, or else in the default install under `Program Files (x86)`. When
`bass.dll` is in neither, the project removes the two files, as it already removes the net48-only
tests from net8.0, and the net48 build prints a line naming the folder and how to bring them back.
No other test needs a local MusicBee: the plugin's tests run against a fake MusicBee.

Recorded for the public release (issue 01 of the release plan).

## Considered options

**A runtime skip.** Rejected: `CLAUDE.md` rules that a test that cannot run is excluded with its
reason, never skipped, so a skipped count never hides a test that stopped running. xunit 2 has
no dynamic skip either, so this would mean another package.

**An opt-in trait or category, filtered out by default.** Rejected: the author, who has
MusicBee, would have to remember the filter, and the default run would stop proving MusicBee's
BASS on the one machine that can.

**A separate test project.** Rejected: it duplicates the net48 x86 setup and the shared helpers
for two files.

## Consequences

- The net48 count differs by machine: 29 fewer without MusicBee. A commit message states the
  counts its run had. The author's machine runs them.
- The rule in `CLAUDE.md` now covers both cases: a test excluded by the project, with its
  reason, for a runtime that cannot run it (net48) or a host that is not installed.
- Pointing `FADEM_MUSICBEE_DIR` at an empty folder proves a stranger's run on the author's
  machine.
