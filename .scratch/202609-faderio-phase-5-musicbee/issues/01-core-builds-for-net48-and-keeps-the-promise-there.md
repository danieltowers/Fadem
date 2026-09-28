# 01: Core builds for net48 and keeps the promise there

Status: resolved
Blocked by: — (can start immediately)

Part of the [MusicBee spike](../spec.md). A prefactor: the plugin can only host an engine that
runs on MusicBee's 32-bit .NET Framework 4.8.

## What to build

`Faderio.Core` builds for `net8.0` and `net48` from one source, and the promise is proven on
net48 x86. Standalone faderio is unchanged.

- **Target net48, not netstandard2.0.** The MP3 reader Core uses ships only in the full NAudio
  package.
- **Mechanical differences live in three places, never in engine logic** (no `#if` there):
  - a compiler-polyfill file: `IsExternalInit`, the `required`-member attributes, `Index` and
    `Range`
  - an extensions file for missing instance methods: `Stream.Read(Span)`, `ReadAtLeast`,
    `GetValueOrDefault`, `TryAdd`, `Order`, `ReplaceLineEndings` and the like
  - call-site helpers, compiling on both targets, for missing statics: `Math.Clamp`,
    `Enum.IsDefined<T>`, `char.IsAsciiDigit`, `Stopwatch.GetElapsedTime`, the two `Path` calls,
    `new Guid(span)`, and span `CompareInfo`
- **Small rewrites on both targets:**
  - `[GeneratedRegex]` becomes a static `Regex`.
  - Code-page registration stays on net8.0 only.
  - Immutable-collection initialisers use `.Empty`.
- **Two behaviours need real net48 code:**
  - **Atomic replace.** Write a temp file, then `File.Replace` when the target exists or a plain
    move when it does not. The write-temp-then-replace guarantee is kept.
  - **The folder scan.** Skip hidden and system entries, and treat reparse points (junctions,
    symlinks) as the net8.0 scan treats link targets.
- **net48 packages:** System.Text.Json, System.Collections.Immutable, System.Memory. The store,
  playlist and settings formats do not change by a byte.
- **The engine keeps streaming in device-sized blocks.** MusicBee has 2 GB of address space.
- **A net48 x86 test target.** `Faderio.Core.Tests` gains one, platform x86, and it compiles:
  - the real-input matrix and the fade accuracy tests
  - the atomic-write, store round-trip and folder-scan tests
  - the fixtures and helpers those need

  Anything else is excluded by project item, not skipped. Tests built on `ChildProcess` stay
  net8.0-only, because it launches children through `dotnet`.
- **Two counts per commit.** From this ticket on, every commit message states net8.0 and net48
  x86 counts, and `CLAUDE.md`'s verification rule says so.

## Acceptance criteria

- [x] Core builds for `net8.0` and `net48` with no `#if` in engine logic.
- [x] Every cell of the real-input matrix passes on net48 x86, the FLAC cells included. They are
      the FLAC package's first run on x86.
- [x] The fade accuracy tests pass on net48 x86.
- [x] Atomic writes on net48 replace an existing file and create a missing one, and a failed
      write leaves the old file intact. Proven by test on net48 x86.
- [x] A folder scan on net48 skips hidden and system entries and junctions exactly as on net8.0.
      Proven by test on net48 x86.
- [x] A store and a settings file written on net48 read back on net8.0, and the reverse, byte for
      byte.
- [x] The full net8.0 suite is green. Standalone faderio needs no smoke run, since no shell code
      changes.
- [x] No net48 test is red or skipped.
- [x] `CLAUDE.md` asks for both counts, and this ticket's commit states them.

## Comments

- Built 2026-09-28. **Core** targets `net8.0;net48`, with System.Collections.Immutable 8.0.0,
  System.Memory 4.5.5 and System.Text.Json 8.0.5 on net48 only. No `#if` anywhere. The
  differences live in `src/Faderio.Core/Compat/`:
  - `Compat.cs`, built for both targets: the static stand-ins (`Clamp`, `IsDefined`,
    `IsAsciiDigit`, `ElapsedSince`, `GuidFrom`, `TrimEndingDirectorySeparator`, and
    `GetFullPath(path, base)` with net8.0's root-relative and drive-relative rules).
  - `Net48/`, built for net48 only: the compiler polyfills (`IsExternalInit`, the required-member
    attributes, `Index`, `Range`) and `CompatExtensions`, the missing instance members.
  - Per-target pairs in `Net8/` and `Net48/`: `FileSystemCompat` and `CodePages`. The project file
    removes the other target's folder.
- **Rewritten on both targets:** `PlaylistFolder`'s two `[GeneratedRegex]` are now static
  `Regex`. `ReplayGain.ParseDb` spells out the `NumberStyles` that net8.0's
  `TryParse(string, IFormatProvider, …)` uses. No immutable collection used a collection
  expression, so nothing needed `.Empty`.
- **Atomic replace:** all four `File.Move(…, overwrite: true)` sites call
  `FileSystemCompat.MoveReplacing`: the store, settings and playlist writes, and both
  move-asides to `.corrupt`. On net48 it uses `File.Replace` when the target exists and
  `File.Move` when it does not.
- **Folder scan:** on net48, hidden and system entries are filtered by attribute. A folder is a
  link when its reparse tag, read with `FindFirstFileW`, is a symbolic link or a mount point.
  Those are the only two tags net8.0's `LinkTarget` answers for, so a cloud placeholder folder is
  listed on both.
- **Tests:** `Faderio.Core.Tests` targets `net8.0;net48`. The net48 target is x86 and builds a
  listed subset:
  - the matrix and fade accuracy tests, with their helpers
  - the store, settings and machine-settings tests, and `StoreFailureTests`
  - four new files:
    - `AtomicWriteTests`: JSON and playlist writes create a missing file, replace an existing
      one, and a write that fails midway leaves the old bytes and no `.tmp`. A move-aside
      replaces an earlier `.corrupt`.
    - `FolderScanTests`: hidden and system files and folders are left out, a nested junction
      is left out whether it loops or leads elsewhere, and a junction asked for by name is
      listed.
    - `FileFormatTests`: store, settings, machine settings and a playlist, with † and ⭐️ in
      paths. Each is written as the checked-in bytes in `Fixtures/formats`, and read back and
      written back unchanged, on both targets. `.gitattributes` marks those fixtures `-text`
      because they are CRLF.
    - `PluginRuntimeTests` (net48 only): the run really is 32-bit .NET Framework 4.8.
- **Two test-side rewrites so the matrix compiles on net48:** array ranges became span slices,
  and `TestAudio.Dot` loads vectors through `MemoryMarshal.Cast`. The arithmetic is unchanged.
- **Checked by mutation:** dropping the net48 hidden filter or the junction check turns
  `FolderScanTests` red on net48. Restored.
- **Left as is:** the net48 build has two nullable warnings (`LibraryLaunch.cs:21`,
  `ReplayGain.cs:24`). .NET Framework's `string.IsNullOrWhiteSpace` has no nullability
  annotations. net8.0 builds clean.
- No shell change, so no smoke run.
