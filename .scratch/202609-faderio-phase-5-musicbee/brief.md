## Improvements

I want to explore porting our playlist transition manager to a musicbee plugin

The playlists would need to be listed in musicbee as enhanced playlists

Is musicbee plugins capable of doing this?

API docs here: https://getmusicbee.com/help/api/

---

## The answer (grilled 2026-09-28)

**Not through MusicBee's own player; yes if the plugin plays with faderio's engine.** Recorded
as [ADR 0012](../../docs/adr/0012-the-musicbee-plugin-hosts-the-engine.md), `accepted` since
the spike below passed on 2026-09-30 (the result is at the end).

### What MusicBee's plugin API allows

Read from the API page and the plugin interface (revisions 53 and 58). getmusicbee.com sits
behind a Cloudflare challenge, so the pages were read through the Wayback Machine:

- API page: https://web.archive.org/web/20251227201041/https://getmusicbee.com/help/api/
- Interface, rev 58: https://github.com/BlitterStudio/MB_SubSonic/blob/HEAD/MB_SubSonic/Interfaces/MusicBeeInterface.cs
- Wiki: https://musicbee.fandom.com/wiki/API_Method_Reference

**It cannot play an authored transition with MusicBee's player.**

- Crossfade is one global switch, `Player_SetCrossfade(bool)`. It has no duration, curve or
  overlap setting.
- Position and seeking are whole milliseconds (`Player_GetPosition`, `Player_SetPosition`).
- Track-change notifications are asynchronous, so a plugin hears of a boundary after it has
  passed.
- There is no DSP or output hook for plugins.

This is the same wall the old plugin hit (`mb_DummyTrackController`, kept outside this repo).
It polled every 250 ms and seeked to trigger MusicBee's global crossfade. It was abandoned
because it "could skip and cut, but it could not actually mix two streams" (v2's
`tmp/planning/202609-faderio-build/sources/prototype/notes.md:25`).

**It can:**

- **Decoded audio.** Open a decoded stream of a file (`Player_OpenStreamHandle`) and mix it
  itself. MusicBee-HQPlayer does this.
- **Playlists.** List, create, edit and delete static playlists, and hear when they change.
  - A plugin cannot mark a playlist or an entry as special. It cannot create auto-playlists.
  - MusicBee has no "enhanced playlist" concept.
- **UI.** Add menu items (`MB_AddMenuItem`), dockable panels, plugin views and its own WinForms
  windows.
- **Selection.** Read the selected tracks (`Library_QueryFilesEx("domain=SelectedFiles")`).
- **Storage.** Keep its own files (`Setting_GetPersistentStoragePath`).

A plugin is a 32-bit .NET Framework DLL. MusicBee.exe is x86 and not large-address-aware, so
it has 2 GB of address space.

### Can Faderio.Core run inside it?

Audited 2026-09-28. **Yes. The work is mechanical, with one real risk.**

- **Targets.** Target `net8.0;net48`, not netstandard2.0, because `Mp3FileReader` ships only in
  `NAudio.dll`.
- **Size.** About 22 files and 250–350 lines:
  - a polyfill file (`IsExternalInit`, the `required` attributes, `Index`/`Range`)
  - an extensions file for missing instance methods
  - call-site rewrites (`Math.Clamp` ×14, `Enum.IsDefined<T>`, `char.IsAsciiDigit`,
    `[GeneratedRegex]` and others)
- **Real code, not swaps.**
  - Atomic replace: `File.Move(…, overwrite: true)` at `AtomicJson.cs:26`, `M3UPlaylist.cs:127`,
    `SettingsFile.cs:117` and `Library.cs:120`.
  - The folder scan's hidden/system/junction rules in `FolderContents.cs`.
- **Memory is fine.** The engine streams in device-sized blocks and never holds a whole track.
  Keep it that way.
- **Output stays out of Core.** Core touches only `IAudioOutput`; WASAPI lives in the App's
  `NAudioAudioOutput`.
- **The risk: dependency loading.** On net48, System.Text.Json and System.Collections.Immutable
  pull in System.Memory and friends. These normally need binding redirects, and a plugin cannot
  add them to MusicBee's config.
- **Shell reuse needs four API swaps:** `TaskDialog`, `PlaceholderText`, the folder dialog and
  `Program`. Fitting into MusicBee's window and message loop matters more.

## Decisions

1. **A second host, not a replacement.** faderio stays standalone and remains where the promise
   is proven. The plugin brings authoring, preview and playback of transitions into MusicBee, and
   MusicBee keeps everything else: library, tagging, UI.
2. **The plugin plays, with `Faderio.Core` in MusicBee's process.**
   - Core multi-targets `net8.0;net48`.
   - MusicBee's player never plays a faderio playlist.
   - Rejected: remote-controlling a faderio process, pre-rendered segments, a second engine on
     BASS, and stopping here. See ADR 0012.
3. **The promise is unchanged.** Core's promise tests run on the net48 x86 build. Test projects
   multi-target, so `dotnet test` runs both targets and the commit message states both counts.
4. **One library.**
   - The plugin opens a faderio library folder chosen in its settings: the same store,
     playlists and listening settings.
   - One app holds a library at a time; the other refuses to open it.
   - Transitions stay keyed by path (ADR 0005), so MusicBee's auto-organise makes the tracks it
     moves **gone**.
5. **Playlists live in the plugin's view.** The "enhanced playlists" are faderio playlists,
   listed and played in a plugin view or dockable panel. Edits there are written back to the
   library. They are not listed in MusicBee's playlist tree: a double-click there would play
   them through MusicBee's player, and the plugin could not catch it in time.
6. **Transitions play only inside faderio playlists.** An album or MusicBee playlist played by
   MusicBee's player plays as MusicBee plays it, even across an authored adjacency.
7. **MusicBee's player stands aside.**
   - Starting playback in the plugin stops MusicBee's player.
   - The plugin has its own transport.
   - MusicBee's now-playing, play counts, scrobbling and media keys do not see faderio
     playback.
8. **Output device.** A per-host setting in the plugin's storage, defaulting to MusicBee's
   current device. It offers the same modes as faderio (WASAPI exclusive, shared, WaveOut).
9. **Adding tracks.** A context-menu item adds MusicBee's selected tracks to the open playlist.
   Every file is probed, and rejections are listed together as **Add folder…** lists them.
   Drag-and-drop comes later.
10. **The shell is shared.**
    - A new multi-targeted WinForms library holds the Presenter, transition editor panel,
      transition manager, playlist sidebar, seek bar and `NAudioAudioOutput`.
    - `MainForm` and `Program` stay standalone-only.
    - The plugin composes its own view from the shared controls, under the same placement
      tests.
11. **Where it lives.**
    - `src/Faderio.MusicBee` in this repo.
    - Dependencies ship in a private subfolder, loaded by an `AppDomain.AssemblyResolve`
      handler. ILRepack (internalised) is the fallback if another plugin's copy wins.
    - The JSON store format does not change.
    - Smoke runs against a portable MusicBee kept outside this repo, as a MusicBee section in
      `docs/smoke.md`.
12. **Glossary.**
    - **Host** enters `CONTEXT.md` when the spike passes: the program playing a library, either
      faderio itself or MusicBee through the plugin.
    - "Enhanced playlist" joins **Playlist**'s _Avoid_ list.

## The spike comes first

A minimal `Faderio.MusicBee` plugin in the portable MusicBee must:

1. load `Faderio.Core` and its dependencies on net48 x86, beside MusicBee's other plugins;
2. open a faderio library chosen in its settings;
3. stop MusicBee's player, play one authored crossfade through its own output in both WASAPI
   shared and exclusive, and release the device afterwards;
4. pass Core's promise tests on net48 x86. That run stays as the permanent test.

If any of the four fails, phase 5's answer is **no**: record why and mark ADR 0012 rejected. If
all pass, ADR 0012 is accepted, **Host** enters the glossary, and the issues below are staged.

## After the spike (issues, not yet written)

- Core multi-targeted in full, with both test targets running every commit
- The shared WinForms library, carved out of the App
- The plugin view: the library's playlists, the open playlist's queue, and transport
- The transition editor and preview in the plugin
- The transition manager in the plugin
- Adding MusicBee's selection to the open playlist
- The output device setting
- One library holder at a time, between faderio and the plugin
- The MusicBee section of `docs/smoke.md`

## Out of scope

- Mirroring faderio playback into MusicBee's now-playing, play counts, scrobbling or media keys.
- Listing faderio playlists in MusicBee's playlist tree.
- Authored transitions under MusicBee's own player.
- Stable track identity against auto-organise.
- Drag-and-drop from MusicBee's track list.
- Formats MusicBee plays that faderio does not promise.

---

## The spike's result (2026-09-30)

**Yes.** All four points hold, and ADR 0012 is accepted. **Host** is in `CONTEXT.md`.

1. **Loading.** `Faderio.Core` and its dependencies load on net48 x86 inside the portable
   MusicBee 3.6.9403, from the plugin's private folder through the assembly-resolve handler,
   beside Theater Mode, SkipToNearEnd and DummyTrackController. ILRepack was not needed.
2. **The library.** The plugin opens the faderio library its settings name, chosen in MusicBee's
   Preferences, and keeps its own machine settings in MusicBee's storage.
3. **Playing and letting go.** The user heard the authored crossfade as in standalone faderio,
   in WASAPI shared and exclusive. faderio's Play stops MusicBee's player. The plugin lets go
   of the device while stopped (issue 06).
   - Shared: MusicBee's own player plays straight after faderio's Stop.
   - Exclusive: the device is free about 0.2 to 0.3 s after Stop, but MusicBee's own player
     shows "unable to start playback" if started at once. It plays a few seconds later or on a
     second try. Accepted by the user as a known limit.
4. **The promise.** Core's promise tests pass on net48 x86 every commit: 290 there, 855 on
   net8.0, at `f705486`.

**Left open, carried into the staged issues** (mostly [issue 13](issues/13-the-output-device-setting.md)):

- With exclusive chosen, MusicBee's start shows "WASAPI exclusive unavailable (Not
  implemented); trying WASAPI shared." every time. Play opens exclusive anyway.
- A Play made while MusicBee's own player was playing fell back to shared once, with the same
  notice. Cause not confirmed.
- After a device loss, the next Play opens in the mode in use, not the mode asked for.
- The plugin has nowhere to show which output mode is in use.

The spike also found and fixed an engine fault standalone faderio shared: an authored fade-out
read as the track ending early (issue 07).

The later issues are staged as 08 to 15 in [issues/](issues/), each `needs-triage`: they need
their own spec before an agent builds them.
