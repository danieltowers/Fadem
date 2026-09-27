# 01: A library on disk

Status: resolved
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

- [x] Opening an empty folder as a library leaves `.faderio/`, `.faderio/playlists/` and, after
      the first authoring, `.faderio/transitions.json` in it. Nothing is written elsewhere.
- [x] Two libraries authored in turn each have only their own transitions.
- [x] A corrupt `transitions.json` in a library is moved to `.corrupt` beside it, as today.
- [x] A read-only folder opens unavailable with a notice; the queue still plays.

## Tests

- **fake:** the four acceptance lines; store tests re-pointed at a temp library and still green.

## Smoke

- None this issue (no shell change).

## Comments

- Built 2026-09-27. **`Library`** (`Faderio.Core.Persistence`) opens a folder: it makes
  `.faderio/` and `.faderio/playlists/` and loads both stores. The corrupt / `.recovered` /
  newer-schema recovery moved into it from `Presenter.OpenTransitionStore`, which is gone, and
  the messages are the same. **It never throws.** The library opens unavailable when the folder
  does not exist (it is not created), when `.faderio/` cannot be made (an ACL-denied folder; the
  read-only attribute does nothing on Windows folders), or when a store cannot be read. Its
  `Notices` name the reason.
- **Engine.** A new constructor `PlaybackEngine(output, Library, sink, rng)`. The old one stays
  for the renderer and tests, and now takes a nullable store. With no store, `Author` /
  `Unauthor` say "Not saved/removed: A → B. The library X could not be opened (…)." (or "No
  library is open."), and every adjacency plays unauthored. **`ChangeSettings` is refused the same
  way**, because there is no file to save to and the rule is "saved, or not saved and nothing
  changes". `View.Library` is `LibraryView(Path, Name, IsAvailable)`, null for an engine outside
  a library.
- **The shell no longer holds the store.** The editor's Render went `Renderer.RenderBoundary(_,
  store, _)` from the shell. It is now `engine.RenderBoundary(adjacency, path)`, so issue 02 can
  swap the store under it. `MainForm` and `TransitionEditorPanel` lost their store parameter.
- **Interim folder.** Until issue 02, `Program.cs` opens `%APPDATA%\faderio` itself as the
  library, so the files are at `%APPDATA%\faderio\.faderio\`. The old
  `%APPDATA%\faderio\transitions.json` and `settings.json` are left alone and no longer read
  (D13). That includes the output mode, which resets to WASAPI shared until chosen again. The
  whole `PlayerSettings` sits in the library file for now; issue 03 moves the machine half out.
  `docs/smoke.md` names the new paths.
- Tests: `LibraryTests` (11: the four acceptance lines, the view's name, the three recovery paths,
  a missing folder, and no library) and two engine-render tests. The store tests, `TestEngine`
  and the form harness now run on a temp library. The two recovery tests left `PresenterTests`.
