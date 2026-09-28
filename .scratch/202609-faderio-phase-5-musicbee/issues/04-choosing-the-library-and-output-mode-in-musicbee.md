# 04: Choosing the library and output mode in MusicBee

Status: resolved
Blocked by: 03

Part of the [MusicBee spike](../spec.md).

## What to build

MusicBee's Configure for the faderio plugin opens a small WinForms dialog with:

- a library folder picker
- an output mode choice: WASAPI exclusive, WASAPI shared, WaveOut

MusicBee's `SaveSettings` persists both to the plugin's machine settings, and the next
`Initialise` reads them back.

- **Changing the library** stops playback and switches the engine's library, as standalone
  faderio switches libraries.
- **Changing the output mode** takes effect as it does in standalone faderio.
- **A chosen folder that is gone** shows its message.

## Acceptance criteria

- [x] Headless (the ticket 03 seam):
  - [x] Settings saved through `SaveSettings` are read back by a fresh `Initialise`.
  - [x] Choosing another library while playing stops playback and opens the new library's open
        playlist.
  - [x] A gone folder gives its message and leaves no library open.
- [x] The MusicBee Looks smoke gains, reported item by item:
  - the dialog opens from MusicBee's plugin preferences
  - a chosen library and output mode survive a MusicBee restart
  - a gone folder shows its message
- [x] The commit states both counts.

## Comments

- Built 2026-09-28.
- **How MusicBee drives it.** `ConfigurationPanelHeight` stays 0, so Preferences → Plugins shows
  a **Configure…** button for faderio. MusicBee calls `Configure(IntPtr.Zero)`, and the plugin
  shows its own dialog and returns true. MusicBee calls `SaveSettings` when Preferences' **Save**
  or **Apply** is pressed.
- **The dialog**, `SettingsDialog`, is owned by `MB_GetWindowHandle()`. It has a folder box with
  **Browse…** (the net48 `FolderBrowserDialog`) and the three modes, named by `OutputModeNames`.
  Its note reads "Applies when MusicBee next starts.". The box is typed as well as browsed. OK
  refuses a relative or malformed path; a full path that does not exist is let through.
  - No field holds a Core value, because MusicBee loads every type before the resolver exists.
    The mode list's items are boxed `AudioOutputMode`s, named in its `Format` event.
    `LoadingTests` still passes.
- **OK waits for Save.** `Host` keeps the dialog's choice as a `MachineSettings` until
  `SaveSettings`, as MusicBee's own preferences wait. A second Configure opens on that pending
  choice. `SaveSettings` sends each change to its own engine call, as standalone faderio's
  Open library and Settings… do:
  - another library: `OpenLibrary`. That stops playback, opens the library's open playlist and
    remembers the folder. A folder that could not open is tried again even if its path is
    unchanged, for a drive that has come back.
  - another mode: `ChangeMachineSettings`. It is saved for the next start, and the engine says
    "Saved machine settings.".
  - nothing chosen, or the same as now: nothing is sent, so an Apply for another preference
    never stops faderio.
- **Seam:** `Plugin.ShowSettings` (show the dialog, say whether OK) and `SettingsDialog.ChooseFolder`,
  both replaced by tests, as `MainForm.ShowSettings` is. The engine's no-library message now
  points to the dialog.
- **Tests,** `PluginTests.Settings.cs`: 8 new, 16 in the project.
  - The dialog opens on the library open and the saved mode, and names the modes.
  - A browsed library and WaveOut, saved, are read back by a fresh plugin: it opens that
    library's open playlist and asks the output factory for WaveOut.
  - Another library while playing: still playing after OK, stopped at Save, and the new open
    playlist loaded.
  - A gone folder gives one message and leaves `LibraryView(gone, IsAvailable: false)` with an
    empty queue.
  - A folder gone at start and back again is opened when it is chosen again.
  - Cancel sends nothing, nor does a Save with nothing chosen.
  - A mode change alone keeps playing and is written to the file.
  - Configure after a failed start repeats why and shows no dialog.
- **Checked by mutation** (each red, then restored):
  - `SaveSettings` sending nothing: 5 red.
  - OK sending at once: the switch test red.
  - A same-path library counted as open when it had not opened: the "back again" test red. That
    mutation survived until that test was added.
- **Found in the smoke:** the mode list read "WasapiShared". `ComboBox.Format` fires only with
  `FormattingEnabled`. A test that reads the list's item texts was red, then fixed.
- **Counts:** Core net8.0 853, Core net48 x86 288, App 274, MusicBee net48 x86 16; 0 failed.
- **MusicBee Looks, portable MusicBee 3.6.9403:**
  - ✅ Preferences → Plugins lists faderio with **Configure…**. It opens the dialog, owned by
    MusicBee's main window, on the settings file's library and "WASAPI shared". **Browse…** opens
    "Browse For Folder" with the dialog's description.
  - ✅ Choosing `library2` and WaveOut, then OK: the file was unchanged. Configure again opened
    on the pending choice. After **Save**, the file named `library2` and WaveOut, `library2` gained
    its `.faderio` folder, and a box read "Saved machine settings.". After a MusicBee restart, the
    dialog opened on `library2` and WaveOut.
  - ✅ Typing a folder that does not exist, with WASAPI shared, then Save: a box owned by MusicBee
    read "The library gone could not be opened (the folder does not exist). Nothing is saved
    until a library opens; what is queued still plays." The file kept `library2`, with the mode
    back to WasapiShared.
  - ✅ A relative path ("music\library") was refused by the dialog, which stayed open. Cancel, then
    closing Preferences, changed nothing.
  - Issue 03's four items, rerun on this build:
    - ✅ Listed and enabled: `MusicBee3Settings.ini` names `mb_faderio.dll` "faderio",
      `<Enabled>true`.
    - ⚠️ **Tools → faderio** was not reached, so it is not observed here. Later in the run,
      MusicBee's main menu closed between posted keys, three times in a row across two fresh
      starts. Another app had the foreground, and focus was not taken from it. The menu code did
      not change, and `Initialise_AddsToolsFaderioWithPlayAndStop` still passes.
    - ⚠️ Play/Stop was not observed, for the same reason. The meter read 0.000 throughout,
      since nothing was pressed.
    - ✅ With `Plugins\faderio\` removed, MusicBee starts and a box reads "faderio could not start:
      Could not load file or assembly 'Faderio.Core, Version=1.0.0.0…'". The folder was put back.
  - The title click that opens MusicBee's menu is now at x = 60. At 75 it missed once
    MusicBee's window was maximised.
- **Worth a look at the verdict:** each notice is a warning box, so a mode change shows
  "Saved machine settings." with a warning icon. That is the notice standalone faderio lists;
  the plugin has no quieter place for it yet.
