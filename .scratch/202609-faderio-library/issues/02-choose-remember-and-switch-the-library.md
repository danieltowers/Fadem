# 02: Choose, remember and switch the library

Status: ready-for-agent
Blocked by: 01

Decided in [D12, D13](../../202609-faderio-interim-improvements/decisions.md), ADR 0011.

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

- [ ] First launch, no remembered library: the folder prompt appears, defaulting to Music.
- [ ] Choose a folder, author a transition, quit, relaunch: same library, transition present.
- [ ] Open library… to a second folder while playing: playback stops, the queue empties, the
      store is the second folder's (empty).
- [ ] `--library=<temp>` skips the prompt and the remembered path.
- [ ] Remembered path deleted → the prompt appears.

## Tests

- **fake:** `OpenLibrary` stops, clears and swaps the store; the unavailable path; launch
  resolution order as a pure function of (arg, remembered, exists).
- **form:** the button calls `OpenLibrary`; the title follows `View.Library`.

## Smoke

- **Looks:** the title bar names the temp library's folder.

## Comments
