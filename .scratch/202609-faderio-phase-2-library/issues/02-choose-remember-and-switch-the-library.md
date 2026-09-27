# 02: Choose, remember and switch the library

Status: resolved
Blocked by: 01

Decided in [D12, D13](../../202609-faderio-phase-2-interim-improvements/decisions.md), ADR 0011.

## What to build

- **`OpenLibrary(string folder)`** on the engine: `Stop()`, `Clear()`, release the current
  store and settings, open the folder as in issue 01, publish. Total: a folder that cannot be
  opened yields the unavailable state and a notice; the previous library is not kept open.
- **Remembering:** the last successfully opened library's path is written to the AppData
  machine settings (issue 03's file; until then, a `library` key in the existing AppData
  `settings.json`).
- **Launch:**
  1. `--library=<path>` on the command line wins (added for smoke runs; the agent points it at
     a temp folder).
  2. Else the remembered path, if it still exists.
  3. Else the shell prompts with a folder dialog whose initial folder is
     `Environment.SpecialFolder.MyMusic`. Cancel leaves the app open with no library: authoring
     refused with a notice, playback allowed.
- **Switching:** an **Open library…** button in the top bar (a folder dialog) calling
  `OpenLibrary`. If the editor has unsaved edits, the existing Save/Discard/Cancel prompt runs
  first. Nothing is migrated (D13).
- **Showing it:** the title bar reads `faderio — <library name>` (the playlists spec puts the
  playlist name in front). When no library is open it reads `faderio — no library`.
- `docs/smoke.md`: the launch line gains `--library=<temp folder>`; the sentence about
  `%APPDATA%\faderio\transitions.json` now names `<library>\.faderio\transitions.json`.

## Where the logic lives

- **Engine:** `OpenLibrary`, the remembered path, the launch resolution order (given the
  command-line value and the dialog's answer by the shell).
- **Shell:** the two dialogs, the button, the title.

## Acceptance

- [x] First launch, no remembered library: the folder prompt appears, defaulting to Music.
- [x] Choose a folder, author a transition, quit, relaunch: same library, transition present.
- [x] Open library… to a second folder while playing: playback stops, the queue empties, the
      store is the second folder's (empty).
- [x] `--library=<temp>` skips the prompt and the remembered path.
- [x] Remembered path deleted → the prompt appears.

## Tests

- **fake:** `OpenLibrary` stops, clears and swaps the store; the unavailable path; launch
  resolution order as a pure function of (arg, remembered, exists).
- **form:** the button calls `OpenLibrary`; the title follows `View.Library`.

## Smoke

- **Looks:** the title bar names the temp library's folder.

## Comments

- Built 2026-09-27. **`OpenLibrary(folder)`** opens the folder off the lock, then in one command
  ends a running add and a preview, stops, empties the queue (Clear's body, now `EmptyQueue`),
  and swaps the store, the settings and the resolver's settings for the new library's. Its
  notices are raised as issue 01's are. A folder that cannot be opened leaves an unavailable
  library, so the previous store is not kept. `RenderBoundary` now reads the store under the
  lock, since it can be swapped.
- **Remembering.** A new `MachineSettings` (`SchemaVersion`, `Library`) in
  `%APPDATA%\faderio\settings.json`, engine-owned through `MachineSettingsStore`. It is the file
  issue 03 grows, so issue 03 adds `OutputMode` to it rather than making it. `SettingsStore`'s
  load, `.corrupt` and newer-schema handling moved into a generic `SettingsFile<T>` that both
  stores use; the library file's notices are unchanged and the machine file's say "machine
  settings file". The old keys in that file (`outputMode` and the rest) are ignored and dropped
  on its first write (D13). The engine writes the path whenever a library opens available, at
  construction or through `OpenLibrary`, and says so in a notice if it cannot.
- **`--library=` remembers nothing.** With it, `Program` builds no machine store, so a smoke run
  neither reads nor writes which library a plain launch opens, even through Open library…. Read
  the issue's "skips the remembered path" that way so an agent's temp folder never becomes the
  user's library.
- **Launch.** `LibraryLaunch.Resolve(argument, remembered, exists)`, a pure function in
  `Faderio.Core.Persistence`. `Program` resolves, shows the folder dialog if it must ask (before
  the window, since the output mode is still read from the library's settings until issue 03),
  and hands the engine the opened library, or none on Cancel.
- **Title.** It reads `faderio — <library> · <mode> at <rate> Hz`. The issue's
  `faderio — <library name>` comes first, and the output mode stays after it, because the
  fallback smoke items read the mode from the title. `faderio — no library · …` with none, and
  `<name> (could not be opened)` for an unavailable one. The form renders it from
  `View.Library` on every tick, so `Program` no longer sets it.
- **Shell.** **Open library…** sits between Save as playlist… and Settings…. It settles unsaved
  edits first (Cancel stops before the folder dialog). Then it asks for the folder, empties the
  editor (new `TransitionEditorPanel.Close()`, since its adjacency belonged to the library left)
  and makes one `OpenLibrary` call. Both folder dialogs start in Music.
- Tests: `SwitchingLibrariesTests` (18): switching while playing, a preview ended, settings
  swapped, a gone folder, remembering and not, the old AppData file, a corrupt machine file,
  choose → author → relaunch, no library, and the launch order as eight cases. Form: the
  title, the button, Cancel, unsaved edits Cancel and Save. Presenter: title and
  `--library=` parsing.
- Acceptance 2, 3 and 5 are proven by the engine tests, not by hand in the app. Acceptance 1
  and 4 were seen in the smoke run.
