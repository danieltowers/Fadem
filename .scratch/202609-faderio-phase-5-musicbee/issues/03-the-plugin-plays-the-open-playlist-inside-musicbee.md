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

- [ ] A new `Faderio.MusicBee.Tests` project (net48 x86) builds the real plugin class. It hands
      the plugin a MusicBee API struct of recording fakes, marshalled to the pointer
      `Initialise` takes, plus a TestTemp library authored through Core's store (two generated
      tracks, an open playlist, one authored crossfade) and the fake output through the
      factory. It proves:
  - [ ] The plugin opens the library its settings name.
  - [ ] Tools → faderio → Play and Stop are registered.
  - [ ] Play calls `Player_Stop` before the fake output pulls a frame.
  - [ ] Across the authored boundary, the frames pulled equal what the engine renders for that
        adjacency.
  - [ ] Stop and `Close` stop and dispose the output.
  - [ ] A missing or gone library gives a notice, not an exception.
- [ ] The fake output is shared from the Core test project, not copied.
- [ ] `docs/smoke.md` gains a MusicBee section run against the portable MusicBee, with its
      Looks half passed and reported item by item:
  - the plugin is listed and enabled
  - Tools → faderio is present
  - with a pre-written settings file, Play starts faderio (MusicBee's player stops) and Stop ends it
  - with the dependency folder removed, MusicBee starts and the plugin reports its load failure
- [ ] The Sounds half is written for the user and not claimed.
- [ ] The commit states both counts.
