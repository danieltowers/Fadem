# Which settings exist, and where do they live?

Type: grilling
Status: resolved
Blocked by: 02, 03, 04, 05, 06

## Question

v2 issue 0013 persisted settings as JSON in the user profile: output mode, default adjacency
behaviour (hard cut, or a global default crossfade of N seconds), skip behaviour (since dropped
by [What do skip, repeat and end-of-queue do?](02-what-do-skip-repeat-and-end-of-queue-do.md)), ReplayGain
on/off, preview pre-roll, visualiser on/off; a `schemaVersion`; missing keys fall back to
defaults; a corrupt file warns and resets. v3 has launch arguments instead
(`--output=`, `--play`, `--edit-first`).

Decide, once tickets 02 to 06 have said which behaviours exist:

- **The list.** Which settings survive, which are new, which are dropped.
- **A default crossfade.** Whether an unauthored adjacency can play anything but a hard cut.
  This touches the glossary's **Hard cut** and the resolver.
- **Output mode changes.** ADR 0008 fixes the engine rate at launch. Does changing the output
  device or mode need a restart, or does the engine rebuild?
- **Storage.** A file beside `transitions.json`, its schema and corruption rule, and what
  happens to the launch arguments (kept for the smoke checklist, or removed).
- **The dialog.** What it shows; it is shell, so it adds Looks items to `docs/smoke.md`.

## Answer

Grilled with the user on 2026-09-23, collecting what tickets 02 to 06 and 09 fed it.
`CONTEXT.md` gains **Default crossfade**.

### The list: three settings

| Setting | Values | Default | Takes effect |
|---|---|---|---|
| Output mode | WASAPI shared / WASAPI exclusive / WaveOut | shared | next launch |
| Unauthored boundaries | *Hard cut*, or *Crossfade for N s* (N 1–30, whole seconds) | hard cut, N = 5 | at once, on any boundary whose transition has not started |
| ReplayGain | on / off | on | at once, on every live voice |

- **Output device:** always the system default. Reopening after device loss already assumes
  it ([What happens when a file or the device fails?](05-what-happens-when-a-file-or-the-device-fails.md)).
- **Dropped from `PlayerSettings`:**
  - `SkipBehaviour` ([What do skip, repeat and end-of-queue do?](02-what-do-skip-repeat-and-end-of-queue-do.md));
  - `PreviewPreRollSeconds` (fixed at 5 s by [What is preview?](04-what-is-preview.md));
  - `VisualiserVisible` (cut by [Does the visualiser come back?](03-does-the-visualiser-come-back.md)).
- **ReplayGain** is as settled in
  [When does ReplayGain switch, and is it on by default?](06-when-does-replaygain-switch-and-is-it-on-by-default.md).

### The default crossfade, and a coupling cut

- **Unauthored adjacencies** still play a hard cut unless the user opts in to a default
  crossfade of N seconds. The engine already supports it; the setting was unreachable.
- **Cut:** `TransitionResolver` (line 105) resolved a *blank authored duration* to
  `DefaultCrossfadeDuration`, so changing N changed transitions the user had authored. A blank
  authored duration now resolves to a **fixed built-in 5 s**, never to N. A global setting
  never alters an authored transition.
- **A change to N or the choice** reschedules any pending boundary whose transition has not
  started, as `Author` already does, and never touches a transition in flight (the
  audible-time rule).

### Output mode needs a restart

The dialog saves the new mode and says it applies at next launch. The engine rate is fixed at
launch (ADR 0008), and a runtime rebuild is too large a change for something set once. When a
fallback is active, the dialog shows the mode actually in use beside the chosen one.

### Storage

- **File:** `%APPDATA%\faderio\settings.json`, beside `transitions.json`, schema version 1,
  through the existing `SettingsStore`.
- **Missing keys** fall back to their defaults.
- **A newer schema** warns (as a notice) and loads defaults.
- **A corrupt file** is moved aside with a notice, as the transition store does, instead of
  today's warn-and-reset. The old settings stay recoverable.
- **Who owns it:** the engine, as it owns the transition store. `PlayerView` carries the current
  settings. `Program` reads only the output mode from the store, before it builds the engine.

### The dialog

- **Settings…** in the main window opens it, with OK / Cancel.
- **OK is one engine call,** `ChangeSettings(settings)`, which saves the file and applies each
  value per the table (placement test 1). The dialog reads its values from the view and holds
  nothing between calls (placement test 3).
- It is shell, so its build issue adds Looks items to `docs/smoke.md`.

### Launch arguments

- **`--output=` is removed.** The dialog replaces it, and a command-line override would be a
  second source of truth.
- **`--play`, `--edit-first` and plain file arguments stay** as smoke-run aids. `docs/smoke.md`
  is updated to match.

### Ruled out of scope: queue persistence across restarts

v2 never did it. Saving the queue as M3U, from
[What does queue management add?](12-what-does-queue-management-add.md), is the manual way.
Automatic restore brings its own questions (unreadable rows, a playhead held across launches),
and returns as a fresh effort if daily use misses it. This clears the map's fog item.
