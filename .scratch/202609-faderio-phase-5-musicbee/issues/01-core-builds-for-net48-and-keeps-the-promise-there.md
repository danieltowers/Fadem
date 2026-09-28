# 01: Core builds for net48 and keeps the promise there

Status: ready-for-agent
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

- [ ] Core builds for `net8.0` and `net48` with no `#if` in engine logic.
- [ ] Every cell of the real-input matrix passes on net48 x86, the FLAC cells included. They are
      the FLAC package's first run on x86.
- [ ] The fade accuracy tests pass on net48 x86.
- [ ] Atomic writes on net48 replace an existing file and create a missing one, and a failed
      write leaves the old file intact. Proven by test on net48 x86.
- [ ] A folder scan on net48 skips hidden and system entries and junctions exactly as on net8.0.
      Proven by test on net48 x86.
- [ ] A store and a settings file written on net48 read back on net8.0, and the reverse, byte for
      byte.
- [ ] The full net8.0 suite is green. Standalone faderio needs no smoke run, since no shell code
      changes.
- [ ] No net48 test is red or skipped.
- [ ] `CLAUDE.md` asks for both counts, and this ticket's commit states them.
