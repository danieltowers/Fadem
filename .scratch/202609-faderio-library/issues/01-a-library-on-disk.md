# 01: A library on disk

Status: ready-for-agent
Blocked by: —

Decided in [D10](../../202609-faderio-interim-improvements/decisions.md), ADR 0011.

## What to build

- A `Library` type in `Faderio.Core` that, given a folder path, knows the paths it owns:
  `<library>/.faderio/transitions.json`, `<library>/.faderio/settings.json`,
  `<library>/.faderio/playlists/`. Opening it creates `.faderio/` and `playlists/` if absent.
- `JsonTransitionStore` and `SettingsStore` are constructed from those paths instead of the
  `%APPDATA%` ones `Program.cs` builds today. Their behaviour (schema version, atomic write,
  `.corrupt` move, `.recovered` fallback, newer-schema handling) is unchanged and their tests
  now run against a temp library folder.
- The library `settings.json` carries **only** the listening settings (issue 03 does the
  split; this issue may land with the whole `PlayerSettings` there and issue 03 moves the
  machine half out).
- A library whose `.faderio/` cannot be created or read (permissions, drive gone) opens as
  **unavailable**: the engine reports a notice naming the reason, holds no store, and refuses
  authoring with a notice until a library opens. Playback of whatever is queued still works.
- `PlayerView` gains `Library` (`LibraryView(Path, Name, IsAvailable)`), `Name` being the
  folder's leaf name. Null before any library has been opened.

## Where the logic lives

- **Engine:** everything above. `Program.cs` stops knowing file names; it hands the engine a
  folder (issue 02 decides which).
- **Shell:** nothing yet; issue 02 shows `View.Library`.

## Acceptance

- [ ] Opening an empty folder as a library leaves `.faderio/`, `.faderio/playlists/` and, after
      the first authoring, `.faderio/transitions.json` in it. Nothing is written elsewhere.
- [ ] Two libraries authored in turn each have only their own transitions.
- [ ] A corrupt `transitions.json` in a library is moved to `.corrupt` beside it, as today.
- [ ] A read-only folder opens unavailable with a notice; the queue still plays.

## Tests

- **fake:** the four acceptance lines; store tests re-pointed at a temp library and still green.

## Smoke

- None this issue (no shell change).

## Comments
