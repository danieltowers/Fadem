# 09: The shared WinForms library

Status: needs-triage
Blocked by: 05

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 10, ADR
0012). Staged at the verdict; it needs its own spec before an agent builds it.

## What to build

`Faderio.Shell` (multi-targeted `net8.0-windows` and `net48`) holds only the audio output
today. The shell's reusable parts move into it from the App: the Presenter, the transition
editor panel, the transition manager, the playlist sidebar and the seek bar. `MainForm` and
`Program` stay standalone-only.

Known from the brief's audit: shell reuse needs four API swaps on net48 (`TaskDialog`,
`PlaceholderText`, the folder dialog and `Program`). Fitting MusicBee's window and message loop
matters more than the swaps.

The moved code stays a shell under `CLAUDE.md`'s three placement tests.

## Acceptance criteria

- [ ] The App builds on the shared library and standalone faderio behaves as before: its suite
      and the Looks half of `docs/smoke.md` pass.
- [ ] The shared library builds for net48 and its controls construct headless on net48 x86.
- [ ] The commit states net8.0 and net48 x86 counts.
