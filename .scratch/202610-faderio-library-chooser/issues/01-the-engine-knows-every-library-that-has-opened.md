# 01: The engine knows every library that has opened

Status: resolved
Blocked by: none

Core only, no shell. See the [spec](../spec.md), D1–D5 and D8.

## What to build

- `MachineSettings.OtherLibraries`: the other known folders, most recently opened first, never
  `Library` itself; `KnownLibraries` is `Library` then them. It stays schema 2, and a file
  without the key reads as `[Library]`, or `[]` when there is no library. `Normalised()`
  removes blanks, repeats and `Library` (`GetFullPath`, case-insensitive).
- `Remember` (`PlaybackEngineLibrary.cs`) moves the opened library to the top of `Libraries`
  as it sets `Library`, in one save. It writes when the order changes, not only when `Library`
  changes. Its failure notice is unchanged.
- Intent `ForgetLibrary(string folder)`: drops the folder from the list and saves. It does
  nothing to the open library or to a folder not on the list. A failed write is a notice.
- Query `KnownLibraries()`: rows of `{ Folder, Name, IsOpen, Exists }` in list order. `Exists`
  is worked out on each call, never cached.

## Acceptance

- [x] A settings.json of schema 2 with only `Library` reads a one-entry list. One with neither
      key reads an empty list.
- [x] Opening A, then B, then A again lists A, B. Opening a folder that cannot be opened, or
      one another host holds, leaves the list as it was.
- [x] `ForgetLibrary` on a listed folder that is not open removes it, and the file says so.
      On the open library or an unknown folder it changes nothing.
- [x] `KnownLibraries()` marks a deleted folder `Exists = false` and the open one
      `IsOpen = true`.
- [x] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments

2026-10-04, built. The list is `OtherLibraries` beside `Library` rather than one list holding
it, so settings saved before the key read back equal, with no migration. `KnownLibrary.List`
is the query's body as a static, so standalone's first launch can list the known libraries
before its engine exists (issue 02's open point). Removing the library a launch reopens, when
it is not the open one, clears `Library`: the next launch asks.
