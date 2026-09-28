# 03: The plugin plays the open playlist inside MusicBee

Status: ready-for-agent
Blocked by: 01, 02

Part of the [MusicBee spike](../spec.md). The tracer bullet: the thinnest path to "does faderio
play inside MusicBee?".

## What to build

A `Faderio.MusicBee` plugin: a net48 x86 class library of plugin type General, named to
MusicBee's `mb_*.dll` convention. Installed by copying its files into MusicBee's plugin folder,
it:

- **Loads beside MusicBee's other plugins.**
  - Its net48 dependencies ship in a private subfolder beside its DLL.
  - An assembly-resolve handler, registered at the start of `Initialise` before any Core type is
    touched, loads them from there.
  - The entry class names no Core type in its fields or signatures.
  - If loading fails, MusicBee still starts and the plugin says why.
- **Reads its own settings.** Core's machine settings store sits at a `faderio` folder inside
  MusicBee's persistent storage path, holding the library folder and the output mode. It never
  touches standalone faderio's machine settings. There is no dialog yet (ticket 04): smoke runs
  pre-write the file.
- **Opens that library** and its open playlist, as standalone faderio's composition root does:
  the output (from ticket 02), the library, an engine sink, the machine settings store.
- **Adds Tools → faderio**:
  - **Play**: MusicBee's `Player_Stop`, then the engine's Play.
  - **Stop**: the engine's Stop.
- **Shows the engine's notices** as message boxes owned by MusicBee's main window, found through
  `MB_GetWindowHandle`. They are posted through a WinForms synchronisation context captured at
  `Initialise`. A missing or gone library is a notice, never an exception.
- **Stops and disposes** the engine and the output on `Close` and `Uninstall`, releasing the
  device.

The plugin is a shell under `CLAUDE.md`'s three placement tests. It holds no engine state
between calls and computes nothing from engine values. Its composition takes an optional output
factory, null in MusicBee, so tests can supply a fake. Nothing else is injectable.

The MusicBee API interface file is vendored at the minimum revision that has every member used.

## Acceptance criteria

- [x] A new `Faderio.MusicBee.Tests` project (net48 x86) builds the real plugin class. It hands
      the plugin a MusicBee API struct of recording fakes, marshalled to the pointer
      `Initialise` takes, plus a TestTemp library authored through Core's store (two generated
      tracks, an open playlist, one authored crossfade) and the fake output through the
      factory. It proves:
  - [x] The plugin opens the library its settings name.
  - [x] Tools → faderio → Play and Stop are registered.
  - [x] Play calls `Player_Stop` before the fake output pulls a frame.
  - [x] Across the authored boundary, the frames pulled equal what the engine renders for that
        adjacency.
  - [x] Stop and `Close` stop and dispose the output.
  - [x] A missing or gone library gives a notice, not an exception.
- [x] The fake output is shared from the Core test project, not copied.
- [ ] `docs/smoke.md` gains a MusicBee section run against the portable MusicBee, with its
      Looks half passed and reported item by item:
  - the plugin is listed and enabled
  - Tools → faderio is present
  - with a pre-written settings file, Play starts faderio (MusicBee's player stops) and Stop ends it
  - with the dependency folder removed, MusicBee starts and the plugin reports its load failure
- [x] The Sounds half is written for the user and not claimed.
- [x] The commit states both counts.

## Comments

- Built 2026-09-28.
- **The plugin**, `src/Faderio.MusicBee`: net48 x86, assembly `mb_faderio`.
  - `MusicBeeInterface.cs` is revision 53, vendored from the old `mb_DummyTrackController` with
    a header and `#nullable disable`. It is the oldest revision on hand, and it has every member
    used.
  - `Plugin` (the `MusicBeePlugin.Plugin` MusicBee looks for) registers the resolver, adds
    Tools → faderio → Play | Stop, and starts `Host`. `MB_AddMenuItem("mnuTools/faderio", null,
    null)` returns a `ToolStripMenuItem`, and the two items are its drop-down items.
  - `Host` composes the engine as `Program` does: `MachineSettingsStore` at `<storage>\faderio\settings.json`,
    `Library.Open` of the folder it names, the output, and the engine.
    - No library named: the plugin shows its own message.
    - A gone folder: opened anyway, so the engine's own notice says so.
  - `MessageBoxes` posts each message to the UI context captured at `Initialise`, with the text
    as the post's state. The box is owned by `MB_GetWindowHandle()`.
  - `PrivateDependencies` resolves any name found in `Plugins\faderio\`, whatever the version
    asked for.
  - A `PluginLayout` target writes `plugin\mb_faderio.dll` and `plugin\faderio\*` beside the
    build output.
- **Found in the first smoke run:** MusicBee calls `Assembly.GetTypes()` on a plugin before
  `Initialise`, so before the resolver exists.
  - Any type that needs Core to load fails the whole plugin. MusicBee logs a
    `ReflectionTypeLoadException`, starts without the plugin, and records it as disabled.
  - Two types did: a sink implementing `IEngineSink`, and an `OutputFactory` delegate over Core
    types (a delegate's signature loads with it).
  - The engine's notices now go through Core's own `ImmediateSink`, and the seam is a
    `Func<AudioOutputMode, Action<string>, IAudioOutput>`.
  - `LoadingTests` pins this down. It copies `mb_faderio.dll` alone into a new AppDomain and
    calls `GetTypes()` as MusicBee does. It was red before the fix.
- **Tests, `tests/Faderio.MusicBee.Tests`** (net48 x86): 8 tests.
  - `FakeMusicBee` fills the API struct with recording delegates and marshals it with
    `StructureToPtr`. `RecordingContext` stands in for MusicBee's UI thread and keeps each
    post, so messages are read without a box being shown.
  - The library is authored through Core: an engine adds two tones, and the store gets a 1 s
    crossfade at 1.5 s. The tests wait for the open playlist's rows through
    `plugin.Host.Engine.View`, which only reads.
  - The frames the fake pulls equal, sample for sample, `Renderer.RenderBoundary` for the
    adjacency with the library's settings and playlist. Both tones sound at 2 s, so it is not
    a hard cut.
  - `FakeAudioOutput` gained `Disposed`.
- **Checked by mutation** (each red, then restored):
  - Play before `Player_Stop`
  - no authored crossfade
  - `Close` not disposing
- **Counts:** Core net8.0 853, Core net48 x86 288, App 274, MusicBee net48 x86 8; 0 failed.
- **MusicBee Looks, in the portable MusicBee 3.6.9403:**
  - ✅ Installed, MusicBee starts with no error dialog. Once it is closed, its settings list
    `mb_faderio.dll` as "faderio", enabled, beside Theater Mode, SkipToNearEnd and
    DummyTrackController.
  - ✅ Tools ends with faderio, whose submenu reads Play | Stop.
  - ✅ With the settings naming a smoke library of two 0.3-amplitude tones, Play made
    MusicBee's audio session peak 0.300, and Stop brought it back to 0.000.
  - ⚠️ "MusicBee's player stops" was not observed. MusicBee's own player could not be started
    under automation: not by a posted click on ▶, `/Play`, `/PlayPause` or Controls →
    Play/Pause. One try with the plugin removed was inconclusive, because the menu walk
    failed. The call order is proven headless; hearing it is in the Sounds half.
  - ✅ With `Plugins\faderio\` removed, MusicBee starts, and a box owned by it reads
    "faderio could not start: Could not load file or assembly 'Faderio.Core, …' or one of its
    dependencies." The plugin stays enabled.
- **Open, for the verdict (05):** `NAudioAudioOutput` opens the device when it is constructed,
  at plugin load, and keeps it through Stop. It lets go only on `Close`.
  - In WASAPI exclusive, that holds MusicBee's device from start-up, whether or not faderio
    ever plays. That breaks the spec's "lets go of the device when stopped" and user story 21.
  - In shared mode, faderio holds an idle stream. That blocks MusicBee only if MusicBee itself
    opens the device exclusively.
  - This ticket's criteria do not cover it, and nothing here changes the output. Written up as
    [issue 06](06-the-plugin-lets-go-of-the-device-while-stopped.md), which now blocks the
    verdict.
