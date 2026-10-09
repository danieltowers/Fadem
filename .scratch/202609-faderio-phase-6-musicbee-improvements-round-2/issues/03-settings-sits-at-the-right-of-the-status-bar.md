# 03: Settings sits at the far right of the status bar

Status: ready-for-agent
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: "Move the settings button into the far right of
the bottom messaging bar".

## What to build

In the plugin, faderio's settings are a flat ⚙ button at the top right, on the track's line
(`PluginView._openSettings`). Move it into the status strip at the bottom (`_status`,
`_noticeCount`, `_host`), at its far right, after the host line. It stays a flat ⚙ that opens the
same settings dialog (`openSettings`), with its tooltip "faderio settings".

**The App too** (decided 2026-10-09). The App's "Settings…" button in the top toolbar
(`MainForm._settings`, beside Open library…) goes; the App gets the same flat ⚙ at the far right
of its status strip, after the notice count and the clips light, opening its settings dialog as
the button did.

## Acceptance

- [ ] The ⚙ is the last thing in the status strip, at its far right, and opens faderio's settings.
- [ ] Gone from the track's line; that line's room goes to the track's name.
- [ ] The host line (library · mode · device) is cut short before the ⚙, never over it.
- [ ] In the App, the ⚙ is the last thing in its status strip and opens Settings; the toolbar has
      no Settings… button.
