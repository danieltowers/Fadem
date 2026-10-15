# 03: The carry setting

Status: ready-for-agent
Blocked by: none

Core and shared shell. See the [spec](../spec.md), decision 8.

## What to build

- A library setting in `PlayerSettings`: "When a move breaks authored transitions", **Ask**
  (default), **Always carry** or **Never carry**. A library without it reads as Ask.
- A setter on the engine for the dialog's **Don't ask again**, in the style of
  `SetEditorFollowsPlayback`.
- The settings dialog shows it beside the default crossfade and the blank duration, in both hosts.

## Tests

On net8.0 and net48 x86: the default, a round trip through `settings.json`, an old file without
the field, and the setter. A form test for the settings dialog.

## Acceptance

- [ ] As above.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts.
- [ ] The **Looks** half of `docs/smoke.md`, reported item by item, plus: the setting shows and
      saves in both hosts.
