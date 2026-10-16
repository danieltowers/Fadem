<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo-white.png">
    <img src="assets/logo-black-strap.png" alt="Fadem" width="400">
  </picture>
</p>

# Fadem
_**Mind the gap between music tracks**_


Fadem is a music player for Windows that lets you edit the transition between any two tracks.
Most players crossfade every track the same way, with one setting for the whole library. In
Fadem, each pair of tracks can transition exactly the way you want.

At every boundary between two tracks you choose:
- where the outgoing track starts to fade
- where the incoming track starts playing from
- how long each fade lasts

You can set these by ear, simply play the track and capture the moment you want it to fade out.

![Fadem's standalone window with a playlist open and the transition editor beside it. On the seek
bar, the fade-out's start mark is pulled earlier, its end mark pulled in, the whole fade-out moved
by its shading, then the fade-in stretched, with the editor's values and the status strip updating
after each drag](docs/images/fade-drag.gif)

Fadem runs in two ways:

- **Standalone**, in its own window (`fadem.exe`).
- **Inside MusicBee**, as a plugin (`mb_fadem.dll`), with a panel in MusicBee's sidebar or its
  'Now Playing' tab.

Both use the same engine and the same library folders.

The plugin isn't a full part of MusicBee. It has its own queue and its own track timeline, and it
doesn't play through MusicBee's player. It's there to make it easy to get tracks from your
MusicBee library into Fadem. Fadem is a proof of concept, ideally these features would be fully integrated with MusicBee.

Fadem is free under the [MIT licence](LICENSE). Third-party licences are in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Install

Download `FademSetup-<version>.exe` from the
[latest release](https://github.com/danieltowers/Fadem/releases/latest) and run it. Choose the
standalone app, the MusicBee plugin, or both. It installs for your user only, so there's no admin
prompt, and it includes everything the app needs. The installer needs 64-bit Windows. Close
MusicBee before you install the plugin; the installer waits until you do.

To uninstall, go to **Settings → Apps**. Uninstalling leaves your libraries alone.


| | Standalone | MusicBee plugin |
|---|---|---|
| Runtime | None (the installed app is self-contained) | .NET Framework 4.8 (ships with Windows 10/11) |
| Host | — | MusicBee 3, 32-bit (tested on 3.6) |

### Standalone

On first launch, pick a library folder (an empty one starts a new library).

### MusicBee

1. Start MusicBee. Go to **Edit Preferences → Plugins → Fadem → Configure…**, choose a library
   folder and click OK. Then click **Save** in Preferences.
2. Go to **View → Arrange Panels**, tick **Fadem** to place the panel, and click **Save**. If you
   also want Fadem in the Now Playing tab, tick **Show Fadem in the Now Playing tab** in
   Configure….

When Fadem starts playing, it stops MusicBee's own player. To add tracks, drag and drop them,
or use **Add to Fadem's open playlist** on MusicBee's right-click menu. You can bind hotkeys to
**Tools → Fadem → Play / Stop**.

## How Fadem works

A **library** is a folder you choose. Fadem keeps its playlists, transitions and settings in a
`.fadem\` subfolder inside it. Your music files stay where they are.

One **playlist** is open at a time, and the queue *is* that playlist. Changes save
automatically.

A **transition** is the handoff you set for an *ordered* pair of tracks, so A → B and B → A are
separate transitions. It plays wherever that pair meets. A playlist can override it, and the
override applies to that playlist only. Any setting the override leaves blank comes from the
global transition settings.

A boundary with no transition (an **unauthored boundary**) plays as a hard cut. If you turn on
the default crossfade for the library, it plays that instead.

Every setting in a transition is optional:

| Setting | If you leave it blank |
|---|---|
| Fade-out point (from start, or **From end**) | One fade-length before the end |
| Fade-out duration | 5 s (**Blank fade durations** setting) |
| Fade-in point | 0:00 |
| Fade-in duration | 5 s (**Blank fade durations** setting) |
| Fade curve: Equal-power, Linear, S-curve | Equal-power (**Default fade curve** setting) |

The incoming track starts the moment the outgoing one begins to fade, so the two play together
during the fade-out. With a fade-out of zero, the outgoing track plays to its end and the next one starts
straight after. The fade-out and fade-in can have different lengths.

## Using Fadem

Between every two rows in the queue sits a **seam button**. It shows **⋈** where a transition is
saved. Where there isn't one, a **+** appears when you point at the row. Click it to open the
**transition editor** on *this track → next track*. The two rows it's editing are tinted and
joined to the editor by a tab. (In MusicBee's sidebar, the editor takes the queue's place
instead.)

In the editor, choose whether to edit the **Global** transition or the one for **This
playlist**. **Capture** sets a point to where the playhead is now. It works while that point's
track is playing. The nudge buttons move a point or a duration by 0.1 s or 1 s. Buttons save straight away, and a
typed value saves when you press Enter or leave the box.

To hear your work, click **Preview**. It plays your edit from 5 s before the transition begins
and leaves your place in the queue alone. Two playback buttons help too. **⇥** (Cue transition)
jumps to 5 s before the next transition. **⤳** (Transition now) starts the next transition
straight away.

Also:

- **Transitions…** lists every transition that includes one track. In the standalone app it's
  the button beside **☰**; in MusicBee it's on the queue's right-click menu.
- The seek bar marks each fade in red, with the fade shaded between its marks. Drag a mark to
  move one end of the fade, or drag the shading to move the whole fade.
- **Sections** group consecutive rows under a coloured header. They never change what plays.

![Fadem's standalone window: a playlist in two sections, the transition editor open on the first
boundary and joined to its two rows by a tinted tab, and the seek bar marking that boundary's fades
in red](docs/images/main-window.png)

To open the settings in the standalone app, click **☰ → Settings…**. In MusicBee, click **⚙** at
the right end of Fadem's status strip. Choose the output mode there: WASAPI shared, WASAPI exclusive or WaveOut.
If your choice isn't available, Fadem falls back to a simpler one. You can also set what
unauthored boundaries play, blank fade durations, the default curve and ReplayGain. The plugin
can also pick an output device.

![The Transitions dialog for one track: each transition it is in, global and playlist values side
by side, with a Preview button on every row](docs/images/transitions.png)

Fadem uses the colours of MusicBee's skin, dark or light. The standalone app uses the skin the
plugin last read on the same computer. If there isn't one, it uses its own dark theme.

## Supported audio

Every point you set plays within 1 ms of where you put it. To keep that promise, Fadem checks
each file when you add it. It rejects any file it can't play exactly, rather than playing it as
best it can.

| Format | Rates | Depths | Channels |
|---|---|---|---|
| WAV (PCM), FLAC | 44.1, 48, 96 kHz | 16, 24 | mono, stereo |
| MP3 (CBR, or VBR with a Xing header) | 44.1, 48 kHz | — | mono, stereo |
| WMA 9 Standard | 44.1, 48 kHz | — | mono, stereo |
| WMA 9 Lossless | 44.1, 48, 96 kHz | 16 or 24 at 44.1 kHz; 24 at 48 and 96 kHz | stereo |

WMA plays through the Windows Media runtime that comes with Windows. On Windows N editions,
install the Media Feature Pack first. The full details are in [`docs/supported-audio.md`](docs/supported-audio.md).

## Where Fadem keeps its files

- `<library>\.fadem\` holds `transitions.json`, `settings.json` and `playlists\*.m3u8`. It also
  holds the column layouts (`columns.json`, `musicbee-columns.json`) and WMA seek indexes
  (`wma-index\`). While a host has the library open, `host.lock` is there too. To back up a
  library, copy this folder.
- Settings for this computer are in `%APPDATA%\fadem\` (standalone) or
  `%APPDATA%\MusicBee\fadem\` (plugin). A portable MusicBee keeps the plugin's settings in its
  own folder.

Only one host can hold a library at a time. If the other one tries, it shows the library as
unavailable.

## Limitations

- Fadem runs on Windows only
- MusicBee must be the 32-bit version
- The standalone app plays on the default audio device only
- Transitions are tied to file paths
- Cue sheets are not supported

## The future

Today the plugin runs beside MusicBee's player rather than inside it. I'd love Fadem's features
to become part of MusicBee itself one day, so every MusicBee user could edit their transitions
in MusicBee's own queue.

## For developers

The code is in four projects:

- `src/Fadem.Core` is the engine
- `src/Fadem.Shell` is the shared WinForms UI
- `src/Fadem.App` is the standalone host
- `src/Fadem.MusicBee` is the plugin

To build, you need the .NET 8 SDK. The plugin also needs the .NET Framework 4.8 targeting pack.

### Build and test

```powershell
dotnet build fadem.sln
dotnet test fadem.sln
```

The Core and Shell tests run on both net8.0 and net48 x86. The App tests run on net8.0 only,
and the MusicBee tests on net48 x86 only. The tests that measure MusicBee's audio DLLs only
compile on a machine with MusicBee installed. If yours isn't in the default place, point
`FADEM_MUSICBEE_DIR` at it.

### Run the standalone from source

```powershell
dotnet run --project src/Fadem.App
```

Or build Release and run `src\Fadem.App\bin\Release\net8.0-windows\fadem.exe`.

For testing, `fadem.exe` also takes command-line options. The smoke checks in
[`docs/smoke.md`](docs/smoke.md) use them:

```
fadem.exe [files or folders…] [--library=<folder>] [--machine=<folder>] [--play] [--edit-first]
          [--unavailable=<modes>]
```

### Install the plugin from source

1. `dotnet build src/Fadem.MusicBee -c Release`
2. Close MusicBee. Copy **both** `mb_fadem.dll` and the `fadem\` folder from
   `src\Fadem.MusicBee\bin\Release\net48\plugin\` into `%APPDATA%\MusicBee\Plugins\`. For a
   portable MusicBee, copy them into `<MusicBee folder>\Plugins\` instead.
3. Set it up in MusicBee as in [Install → MusicBee](#musicbee).

### Build the installer

You need Inno Setup 6 (`winget install JRSoftware.InnoSetup`):

```powershell
.\installer\build-installer.ps1   # writes artifacts\installer\
```

The version comes from the latest `v*` tag, such as `v1.2.3`. With no tag yet, it's 0.0.0.

## How it was built

As a software developer and Hi-Fi enthusiast, I'd wanted to build a crossfade editor for my playlists for many years, 
but never had the time. Claude changed that: it wrote most of the code, tests and docs. I chose what to
build, settled every design question, and judged by ear what the tests aren't able to hear.

The AI wasn't left to its own devices, each phase starts with a brief and a grilling session.
The result is then broken into issues in [`.scratch/`](.scratch/).
[`CONTEXT.md`](CONTEXT.md) defines the terms that every document, type and conversation uses,
and the [ADRs](docs/adr/) record each design choice, the options turned down, and why.

Tests come first. Much of the suite renders real audio through the engine to check where the
fades land. The engine and shared-UI tests run on both .NET 8 and .NET Framework 4.8 x86, and
every commit message states the pass counts. Research into the engine ends as a permanent test,
with notes in [`docs/research/`](docs/research/).

Some checks need a person. [`docs/smoke.md`](docs/smoke.md) splits them in two: *Looks*, which
the agent runs, and *Sounds*, which only a human can sign off.

Special thanks to **Matt Pocock**, whose [AI skills](https://github.com/mattpocock), including
`wayfinder` and `grill-me`, helped bring this project to life.
