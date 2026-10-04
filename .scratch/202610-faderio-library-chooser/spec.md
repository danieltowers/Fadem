# Spec — faderio v3: the library chooser

Status: resolved
Blocked by: none

Raised 2026-10-04: "library chooser in settings - it needs to remember the previous libraries
and list them, just like the musicbee library selection screen."

## Problem

faderio remembers one library: `MachineSettings.Library`, the last one that opened. Choosing any
other library means finding its folder again from scratch:

- **Standalone.** **Open library…** and the first-launch prompt go straight to a
  `FolderBrowserDialog` (`MainForm.ShowLibraryDialog`), which opens in Music every time.
- **Plugin.** Configure's *Library folder* is a text box with **Browse…**
  (`SettingsDialog._library`). You have to type the path or browse to it.

A user who switches between a few libraries, for example a gig set and a home library, browses
for the folder every time.

## Solution

The engine remembers **every library that has opened on this machine**, not just the last one.
Both hosts pick a library from **one shared chooser** that lists them, the same idea as
MusicBee's library selection screen:

```
┌ Choose a library ───────────────────────────────────────────┐
│  Name             Folder                                    │
│ ▸ Gig set         D:\Music\Gigs\2026                (open)  │
│   Home            C:\Users\…\Music\faderio                  │
│   Old laptop      E:\faderio-old                (not found) │
│                                                             │
│ [Add folder…]  [Remove from list]          [Open] [Cancel]  │
└─────────────────────────────────────────────────────────────┘
```

- Each row shows the library's **name** (its folder's name, `Library.Name`), its **folder**, and
  whether it is **open** now or **not found**. A row that is not found is greyed. You can still
  select it and Remove it, but you can't Open it.
- **Open** (or a double-click, or Enter) chooses the selected library. **Add folder…** is
  today's folder browser. The folder it picks is chosen straight away, as Open would be.
- **Remove from list** forgets a library. Its folder and its `.faderio\` are never touched.
  Remove is disabled on the open library, because opening it again would just put it back.
- **Cancel** chooses nothing.

### Where it shows

| Host | Today | With this spec |
|---|---|---|
| Standalone, **Open library…** | folder browser | the chooser |
| Standalone, launch with nothing to reopen | folder browser | the chooser. The list may be empty (only **Add folder…**), or hold libraries that are not found any more |
| Plugin, Configure | text box + **Browse…** | the library's folder (read-only) + **Choose…**, which shows the chooser. The choice still waits for Save (see below) |

The standalone **Settings…** dialog does not get a library row: **Open library…** already sits
on the queue bar.

## Decisions (the recommendations, accepted 2026-10-04)

- **D1 — Machine state, per host.** The list goes in `MachineSettings` as `OtherLibraries`,
  next to `Library`: the known libraries are `Library` then the others (`KnownLibraries`). Each host keeps its own machine settings (CONTEXT.md, *Host*), so standalone and the
  plugin each have their own list.
- **D2 — Most recently opened first.** Opening a library moves it to the top.
- **D3 — No cap.** Nothing drops off the list. The user removes libraries themselves, as in
  MusicBee.
- **D4 — Only libraries that opened are listed.** A folder that could not be opened (gone,
  unreadable, or held by the other host) is never added. That matches what `Remember` does
  today. `--library=` without `--machine=` still writes nothing to the machine settings.
- **D5 — Schema 2 still.** The new key works like `OutputDevice` did. `OtherLibraries` never
  holds `Library`, so a file without the key knows the `Library` it has, and nobody upgrades to
  an empty list. `Normalised()` removes blanks, repeats and `Library` from it (paths compared
  case-insensitively after `GetFullPath`).
- **D6 — Remove is immediate, in both hosts.** Removing is list upkeep, not a setting, so it
  doesn't wait for the plugin's Save.
- **D7 — The plugin's choice still waits for Save.** Choosing a library in the plugin's chooser
  only fills Configure's library row. `Host.SaveSettings` opens it, as it does now. The chooser
  shows that pending choice as selected.
- **D8 — "Not found" is checked when the chooser asks, not on every view.** A list query
  checks `Directory.Exists` for each folder. Doing that in the view rebuilt on every render
  would touch every folder on the list, including network paths. Whether another host holds a
  library is not checked ahead of time. Opening it gives today's notice naming the holder.

## Placement (the three CLAUDE.md tests)

- **Engine:** `MachineSettings.OtherLibraries` and its normalisation. `Remember` moves the opened
  library to the top. One intent `ForgetLibrary(folder)`. One query `KnownLibraries()`, which
  returns rows of `{ Folder, Name, IsOpen, Exists }` already in order. The shell does no
  ordering, comparing or existence checks.
- **Shared shell (`Faderio.Shell`):** `LibraryChooser` renders the rows and returns the folder
  chosen, or null. It holds the rows only while it is shown. Remove calls the engine, then
  queries again.
- **Hosts:** standalone uses the chooser in place of `ShowLibraryDialog`. The plugin's
  Configure uses it in place of the text box. `ChooseLibrary` and `ChooseFolder` stay as the
  points where tests stub the dialog.
- Net48: the chooser is in the shared shell, so its tests run on net8.0 and net48 x86.

## New term for CONTEXT.md

**Known libraries** (added with issue 01).

## Order

| # | Issue | Blocked by |
|---|---|---|
| 01 | [The engine knows every library that has opened](issues/01-the-engine-knows-every-library-that-has-opened.md) | — |
| 02 | [Standalone chooses from the known libraries](issues/02-standalone-chooses-from-the-known-libraries.md) | 01 |
| 03 | [The plugin's Configure chooses from the known libraries](issues/03-the-plugins-configure-chooses-from-the-known-libraries.md) | 02 |

## Out of scope

Renaming a library (MusicBee lets you name one; here the name is the folder's), creating a new
empty library from the chooser apart from picking an empty folder with **Add folder…**, a
library list shared between the two hosts, and showing which
host holds a library before it is opened.

## Answered questions (2026-10-04: "go with the recommendations")

1. **Standalone Settings…** gets no library row: **Open library…** is enough.
2. **One list per host**, each in its own machine settings.
3. **Most recently opened first.**
4. **Remove in the plugin is immediate**, not waiting for MusicBee's Save.
