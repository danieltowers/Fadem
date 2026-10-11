# Spec — faderio v3 phase 5: the MusicBee spike

Status: ready-for-agent
Blocked by: none
Brief: [brief.md](brief.md) · Decision: [ADR 0012](../../docs/adr/0012-the-musicbee-plugin-hosts-the-engine.md) (proposed)

## Problem Statement

I listen and manage my music in MusicBee, but authored transitions only play in faderio.
MusicBee's player cannot play one: its plugin API offers only a global crossfade switch,
millisecond seeks and track-change notifications that arrive after the boundary. The old plugin
failed on exactly this. The brief found another way: the plugin runs faderio's own engine inside
MusicBee's process. Nobody knows yet whether that works. It depends on four things that no one
has tried:

- `Faderio.Core` loading in a 32-bit .NET Framework 4.8 process
- its dependencies loading without binding redirects, beside MusicBee's other plugins
- the plugin's own output taking and releasing the device MusicBee also uses
- the authoring promise holding on that runtime

Until those are proven, specifying the editor, manager or playlist view inside MusicBee would
mean building on a guess.

## Solution

A minimal `Faderio.MusicBee` plugin that answers yes or no. Installed in MusicBee, it:

- opens the faderio library named in its settings
- stops MusicBee's player
- plays the open playlist through faderio's engine and its own output, so an authored
  crossfade in that playlist plays as authored
- lets go of the device when stopped, in WASAPI shared and exclusive mode

`Faderio.Core` builds for net48 as well as net8.0, and the promise tests pass on net48 x86.

If all of this holds, ADR 0012 is accepted, **Host** enters `CONTEXT.md`, and the rest of phase 5
is staged as issues. If any of it fails, phase 5 answers the brief with **no**: record why and mark
ADR 0012 rejected.

This spike has no transition editor, manager, playlist view or track adding. The library it plays
is authored in standalone faderio.

## User Stories

1. As a listener, I want to install faderio into MusicBee by copying its plugin files into
   MusicBee's plugin folder, so that I do not need an installer.
2. As a listener, I want the plugin to load beside the MusicBee plugins I already use, so that
   trying faderio breaks nothing else.
3. As a listener, I want MusicBee to start normally if the faderio plugin cannot load its
   dependencies, with the plugin saying why, so that a failed spike never costs me my player.
4. As a listener, I want to choose a faderio library folder in the plugin's settings, so that
   MusicBee plays the same library standalone faderio authors.
5. As a listener, I want the plugin to remember that library across MusicBee restarts, so that I
   choose it once.
6. As a listener, I want the plugin to keep its own settings in MusicBee's plugin storage, not in
   standalone faderio's machine settings, so that the two hosts never overwrite each other's
   output mode or remembered library.
7. As a listener, I want to choose the plugin's output mode (WASAPI exclusive, WASAPI shared,
   WaveOut), so that I can match how I run MusicBee.
8. As a listener, I want the plugin to open its library's open playlist when it loads, so that
   Play has something to play.
9. As a listener, I want a faderio command in MusicBee's Tools menu that plays the open playlist,
   so that I can start faderio playback without a playlist view.
10. As a listener, I want faderio's Play to stop MusicBee's player first, so that the two players
    never sound at once or fight over the device.
11. As a listener, I want an authored crossfade in the open playlist to play exactly as it plays
    in standalone faderio, so that I trust what I hear in MusicBee.
12. As a listener, I want unauthored adjacencies to play as the library's settings say (hard cut or
    default crossfade), so that the library behaves the same in both hosts.
13. As a listener, I want the library's ReplayGain setting to apply, so that levels match
    standalone faderio.
14. As a listener, I want a faderio Stop command in the Tools menu, so that I can end faderio
    playback.
15. As a listener, I want Stop to release the audio device, so that MusicBee's own player can play
    again straight away, even in exclusive mode.
16. As a listener, I want closing MusicBee while faderio plays to stop playback and release the
    device cleanly, so that nothing is left holding the sound card.
17. As a listener, I want disabling or uninstalling the plugin from MusicBee's preferences to stop
    playback and release the device, so that the plugin can always be switched off.
18. As a listener, I want the engine's notices (an unreadable track, a fallback to a lesser output
    mode, a store that failed to load) shown to me in MusicBee, so that faderio never fails
    silently inside another app.
19. As a listener, I want a clear message if no library is chosen or the chosen folder is gone,
    so that Play doing nothing is explained.
20. As a listener, I want the plugin to fall back from exclusive to shared to WaveOut as
    standalone faderio does, and tell me which mode it opened, so that a device MusicBee holds
    does not leave me in silence.
21. As a listener, I want MusicBee's player to be untouched while faderio is stopped, so that
    installing the plugin changes nothing about MusicBee until I use it.
22. As the maintainer, I want `Faderio.Core` to build for net8.0 and net48 from one source, so
    that the plugin and standalone faderio run the same engine.
23. As the maintainer, I want the net48 build of Core to keep streaming in device-sized blocks,
    so that it fits MusicBee's 2 GB address space.
24. As the maintainer, I want the store, playlist and settings files written on net48 to be
    byte-for-byte the format net8.0 writes, so that a library moves freely between the hosts.
25. As the maintainer, I want atomic writes to stay atomic on net48, so that a crash inside
    MusicBee cannot corrupt a library's store.
26. As the maintainer, I want a folder scan on net48 to skip hidden and system entries and
    junctions exactly as on net8.0, so that the add behaves the same in both hosts once adding
    arrives.
27. As the maintainer, I want the real-input matrix and the fade accuracy tests to pass on net48
    x86, so that the promise is proven on the runtime the plugin runs on.
28. As the maintainer, I want `dotnet test` to run the net48 x86 promise tests alongside the
    net8.0 suite, and the commit message to state both counts, so that the second target is
    checked every commit.
29. As the maintainer, I want the plugin tested headless through its own entry point with a fake
    MusicBee and a fake output, so that its behaviour is proven without MusicBee installed.
30. As the maintainer, I want the plugin to reach the engine only through the engine's public
    members, under the three placement tests, so that it is a second shell and never a second
    engine.
31. As the maintainer, I want the plugin's net48 dependencies loaded from a private folder by an
    assembly-resolve handler, so that they load without binding redirects and without colliding
    with another plugin's copies.
32. As the maintainer, I want ILRepack (internalised) kept as the recorded fallback if another
    plugin's copy of a dependency wins the load, so that the spike has a next step, not a dead end.
33. As the maintainer, I want the audio output available to both hosts from one source, so that
    exclusive, shared and WaveOut behave identically in both.
34. As the maintainer, I want a MusicBee section in `docs/smoke.md`, so that loading, playing and
    releasing the device inside the real MusicBee are checked by a person each time the plugin
    changes.
35. As the maintainer, I want the spike's outcome recorded in the brief, ADR 0012's status and
    `CONTEXT.md`, so that the next session knows whether phase 5 continues.
36. As a future session, I want each failed spike point written down with what was tried, so that
    nobody retries it blind.

## Implementation Decisions

- **The engine does not change behaviour.** Core gains a second target framework, `net48`,
  beside `net8.0`, built from the same source. The plugin constructs `PlaybackEngine` exactly as
  standalone faderio's composition root does: an output, the library, an engine sink and a
  machine settings store.
- **Core targets net48, not netstandard2.0**, because the MP3 reader Core uses ships only in the
  full NAudio package, whose Framework build is net472.
- **Porting Core is mechanical, and stays so.**
  - One compiler-polyfill file: `IsExternalInit`, the `required`-member attributes, `Index` and
    `Range`.
  - One compatibility-extensions file for missing instance methods: `Stream.Read(Span)`,
    `ReadAtLeast`, `GetValueOrDefault`, `TryAdd`, `Order`, `ReplaceLineEndings` and similar.
  - Call-site helpers that compile on both targets for missing statics: `Math.Clamp`,
    `Enum.IsDefined<T>`, `char.IsAsciiDigit`, `Stopwatch.GetElapsedTime`, two `Path` calls,
    `new Guid(span)`, and span `CompareInfo`.
  - `[GeneratedRegex]` becomes a static `Regex` on both targets.
  - Code-page registration applies on net8.0 only.
  - Collection-expression initialisers of immutable collections use `.Empty`.
  - No `#if` in engine logic: the differences live in the polyfill and helper files.
- **Two behaviours need real net48 code.**
  - The atomic replace uses `File.Replace` when the target exists (and a plain move when it
    does not), keeping the write-temp-then-replace guarantee.
  - The folder scan filters hidden and system attributes and treats reparse points as the
    net8.0 scan treats link targets.
- **net48 dependencies are packages, not rewrites**: System.Text.Json, System.Collections.Immutable
  and System.Memory. The JSON store format does not change.
- **The audio output moves out of the App** into a new multi-targeted library (`net8.0-windows`
  and `net48`). Both the App and the plugin reference it. This library is the seed of the shared
  shell library that later phase 5 issues grow. In the spike it holds the output only.
- **The plugin** is a new project, `Faderio.MusicBee`: a net48 x86 class library named to
  MusicBee's `mb_*.dll` convention, of plugin type General.
  - `Initialise` registers its menu items, starts its dependency resolver, reads its machine
    settings and opens the library. `Close` and `Uninstall` stop and dispose the engine and the
    output.
  - **Dependency loading.** The plugin's dependencies ship in a private subfolder beside its
    DLL. An assembly-resolve handler, registered before any Core type is touched, loads them from
    there. The plugin's entry class references no Core type in its field or signature list, so
    the handler is in place before the runtime needs one.
  - **Machine settings.** The plugin uses Core's machine settings store at a `faderio` folder
    inside MusicBee's persistent storage path. It holds the library folder and the output mode;
    window geometry is unused.
  - **Configuration.** MusicBee's plugin configuration opens a small WinForms dialog: a library
    folder picker and an output mode choice. Changing the library stops playback and switches
    the engine's library, as standalone faderio does.
  - **Commands.** Two items under a faderio submenu of MusicBee's Tools menu:
    - Play open playlist: calls MusicBee's `Player_Stop`, then the engine's Play.
    - Stop: calls the engine's Stop.
  - **The open playlist** is the library's remembered open playlist, as standalone faderio opens
    it. The spike has no way to pick another; standalone faderio chooses it.
  - **Notices.** The engine sink posts to MusicBee's UI thread through a WinForms
    synchronisation context captured at `Initialise`. Notices are shown as message boxes owned
    by MusicBee's main window, which is found through the API's window-handle member.
  - **Placement.** The plugin is a shell under `CLAUDE.md`'s three tests. It holds no engine
    state between calls and computes nothing from engine values. "Play in faderio" is one engine
    call; the preceding `Player_Stop` is MusicBee's, not the engine's.
- **MusicBee API surface used**: `Setting_GetPersistentStoragePath`, `MB_AddMenuItem`,
  `Player_Stop`, `MB_GetWindowHandle`, and the plugin lifecycle methods (`Initialise`,
  `Configure`, `SaveSettings`, `Close`, `Uninstall`, `ReceiveNotification`). The interface file
  is vendored at the minimum API revision that has all of these. No playlist, library, output
  device or stream-handle member is used in the spike.
- **An internal seam for tests.** The plugin's composition takes an optional output factory. In
  MusicBee it is null and the plugin opens the real output. A test passes one returning a fake.
  Nothing else is injectable.

## Testing Decisions

- **What a good test is here.** It drives the plugin or the engine from outside, through what
  MusicBee or the listener does. It asserts what comes out: frames, the calls made to MusicBee,
  files on disk, notices. It never asserts which helper ran or how a polyfill is written.
- **Seam 1: the real-input matrix on net48 x86 (existing seam).**
  - `Faderio.Core.Tests` gains a `net48` target, platform x86.
  - In the spike, that target compiles the promise tests only: `RealInputMatrixTests`,
    `FadeEngineAccuracyTests`, and the fixtures and helpers they need.
  - Checked-in fixtures, test files under the per-run TestTemp folder, no skips.
  - The later issue "Core multi-targeted in full" widens it to the whole suite. Until then no
    net48 test is red and none is skipped. A test that cannot compile there yet is excluded by
    project item, not marked skipped.
  - `ChildProcess` starts children through `dotnet`, which a net48 test binary cannot use, so
    tests built on it stay net8.0-only.
- **Seam 2: the plugin through its own entry point (new, the only new seam).**
  - A new `Faderio.MusicBee.Tests` project (net48 x86) builds the real plugin class. It fills a
    MusicBee API struct with recording fakes and marshals it to the pointer `Initialise` takes.
  - It uses a TestTemp library authored through Core's own store API: two generated tracks, an
    open playlist, one authored crossfade.
  - It supplies the fake output through the output factory.
  - It asserts:
    - The plugin opens the library its settings name.
    - Its menu items are registered.
    - Play calls `Player_Stop` before the fake output pulls a frame.
    - The frames the fake output pulls across the authored boundary equal those the engine
      renders for that adjacency.
    - Stop and `Close` stop and dispose the output.
    - A missing or gone library produces a notice, not an exception.
    - Settings written through `SaveSettings` are read back by the next `Initialise`.
  - The fake output is shared from the Core test project rather than copied.
- **Seam 3: the person, in the portable MusicBee (existing seam).**
  - A new MusicBee section of `docs/smoke.md`.
  - **Looks** (the agent's):
    - The plugin appears in MusicBee's plugin list, enabled.
    - The faderio submenu is under Tools.
    - The configuration dialog opens, keeps a chosen library and output mode across a restart,
      and shows the message for a gone library.
    - MusicBee starts with the dependency folder removed and shows the plugin's load failure.
  - **Sounds** (the user's):
    - The authored crossfade is heard as in standalone faderio.
    - MusicBee's own player plays again straight after faderio's Stop, in WASAPI shared and in
      WASAPI exclusive.
    - Playback survives MusicBee's other plugins being enabled.
- **Prior art**:
  - `RealInputMatrixTests` and `docs/supported-audio.md` for the promise.
  - `TestEngine` and `FakeAudioOutput` for driving the engine headless.
  - `HeadlessFormTests` for constructing a real shell object on an STA thread without showing it.
  - `StoppingTheDeviceTests` and `DeviceLossTests` for output release and fallback.
  - `RendererTests` for comparing frames with what the engine renders for an adjacency.
- **The commit rule** gains a second count: every commit states net8.0 and net48 x86 results.
  Any session that touches the plugin runs the MusicBee Looks half.

## Order

| # | Issue | Blocked by |
|---|---|---|
| 01 | [Core builds for net48 and keeps the promise there](issues/01-core-builds-for-net48-and-keeps-the-promise-there.md) | — |
| 02 | [The audio output moves into a shared library](issues/02-the-audio-output-moves-into-a-shared-library.md) | 01 |
| 03 | [The plugin plays the open playlist inside MusicBee](issues/03-the-plugin-plays-the-open-playlist-inside-musicbee.md) | 01, 02 |
| 04 | [Choosing the library and output mode in MusicBee](issues/04-choosing-the-library-and-output-mode-in-musicbee.md) | 03 |
| 05 | [The spike's verdict](issues/05-the-spikes-verdict.md) (the user's) | 03, 04, 06 |
| 06 | [The plugin lets go of the device while stopped](issues/06-the-plugin-lets-go-of-the-device-while-stopped.md) (found in 03) | 03 |
| 07 | [An authored fade-out is reported as the track ending early](issues/07-an-authored-fade-out-is-reported-as-the-track-ending-early.md) (the engine's, found in 06) | — |

Staged at the verdict (2026-09-30, pass), each `needs-triage` until it has its own spec:

| # | Issue | Blocked by |
|---|---|---|
| 08 | [Core's whole suite on net48](issues/08-cores-whole-suite-on-net48.md) | 05 |
| 09 | [The shared WinForms library](issues/09-the-shared-winforms-library.md) | 05 |
| 10 | [The plugin view: playlists, queue and transport](issues/10-the-plugin-view.md) | 05, 09 |
| 11 | [The transition editor and preview in MusicBee](issues/11-the-transition-editor-and-preview-in-musicbee.md) | 05, 09, 10 |
| 12 | [The transition manager in MusicBee](issues/12-the-transition-manager-in-musicbee.md) | 05, 09, 10 |
| 13 | [The output device setting](issues/13-the-output-device-setting.md) | 05 |
| 14 | [Adding MusicBee's selection to the open playlist](issues/14-adding-musicbees-selection-to-the-open-playlist.md) | 05, 10 |
| 15 | [One library holder at a time](issues/15-one-library-holder-at-a-time.md) | 05 |

## Out of Scope

- Everything after the spike, each a later issue:
  - Core's whole suite on net48
  - the shared WinForms library beyond the output
  - the plugin view with playlists, queue and transport
  - the transition editor, preview and transition manager in MusicBee
  - adding MusicBee's selection to the open playlist
  - the output device setting beyond the mode
  - one library holder at a time
- Choosing or switching the open playlist from inside MusicBee.
- Anything shown in MusicBee's playlist tree.
- Mirroring faderio playback into MusicBee's now-playing, play counts, scrobbling or media keys.
- MusicBee's player playing authored transitions.
- ILRepack, unless the assembly-resolve route fails.
- 64-bit MusicBee.

## Further Notes

- The portable MusicBee for smoke runs is kept outside this repo.
  The old plugin's vendored interface file (revision 53) is at
  the old plugin's `mb_DummyTrackController\MusicBeeInterface.cs` (kept outside this repo). Newer
  copies (revision 58) are linked in the brief.
- MusicBee.exe is x86 and not large-address-aware: 2 GB of address space. The engine's streaming
  keeps it to tens of MB. Any change that buffers a whole track breaks the plugin.
- The FLAC package's unsafe decoder has not run on x86 before. The FLAC cells of the matrix on
  net48 x86 are its first proof.
- Build beside a running faderio with `--artifacts-path`, and run test projects separately, as
  standalone work already does.
- On success:
  - set ADR 0012 to `accepted`
  - add **Host** to `CONTEXT.md`: "the program playing a library: faderio itself, or MusicBee
    through the plugin"
  - append the result to the brief
  - stage the later issues in this folder

  On failure, append which point failed and what was tried, and set ADR 0012 to `rejected`.
