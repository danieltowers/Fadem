# 02: Standalone chooses from the known libraries

Status: resolved
Blocked by: 01

Shared shell plus standalone. See the [spec](../spec.md): the mock-up and "Where it shows".

## What to build

- `Faderio.Shell.LibraryChooser`: a modal list of `KnownLibraries()` rows (Name, Folder, and
  *open* / *not found*). The buttons are **Add folder…**, **Remove from list**, **Open** and
  **Cancel**. Double-click and Enter open the selected row. A row that is not found is greyed
  and can't be opened. Remove is disabled on the open row, calls `ForgetLibrary`, then
  queries again. It returns the folder chosen, or null.
- The folder browser behind **Add folder…** starts in Music, and keeps the hook tests stub.
- `MainForm.ChooseLibrary` and the first-launch prompt in `Program.cs` show the chooser
  in place of `ShowLibraryDialog`. At the first launch the engine isn't built yet: the chooser
  lists `KnownLibrary.List(machine.Settings, null, Directory.Exists)`, the same Core query, and
  Remove there is `machine.Forget(folder)`, the call the engine's `ForgetLibrary` makes.
- `docs/smoke.md` Looks: the chooser's items.

## Acceptance

- [x] **Open library…** shows the chooser with the known libraries, the open one marked.
      Opening another switches library, as today.
- [x] A library whose folder was deleted shows *not found*, greyed. Open is disabled, Remove
      works.
- [x] **Add folder…** picks a new folder, which opens and appears at the top next time.
- [x] A launch whose remembered library is gone shows the chooser, not the bare folder browser.
- [x] Shell tests stub the dialog (net8.0 and net48 x86). The Looks half of the smoke is run and
      reported item by item.

## Comments

2026-10-04, built. Smoke (Looks, the chooser's items) run against the built faderio.exe with
`--machine=` on a temp folder: the list, *open* and *not found* marks, Remove written at once,
Open switching library and moving it first, Add folder… through the real folder dialog, and
the empty launch chooser with Cancel opening no library all checked. Double-click to open was
not driven in the real app (posted mouse input needs faderio topmost); Enter and double-click
both come through `ItemActivate`. The smoke caught Open and Cancel wrapping onto two lines,
fixed. At a launch the chooser has no owner, so it shows in the taskbar, centred on screen.

While running the suite, the section tests' `Application.EnableVisualStyles()` was found to
take effect only when no test before them had made a window: it now runs once as the Shell
test assembly loads (`VisualStyles.cs`). HEAD already failed one section test that way.
