# Spec — faderio v3: the library

Status: ready-for-agent
Blocked by: [interim-transport](../202609-faderio-interim-transport/spec.md)

## Problem

There is one player with one store in `%APPDATA%\faderio\`. Overrides have nothing to be keyed
by, nothing can be carried to another machine, and a playlist is any file the user points at.

## Solution

A **Library** is a folder the user chooses; everything faderio writes goes to
`<library>/.faderio/`. Recorded in [ADR 0011](../../docs/adr/0011-the-library-folder-owns-the-store.md);
decided as D10–D13 in [decisions.md](../202609-faderio-interim-improvements/decisions.md).
**When an issue and that file disagree, the decisions file wins.** Second of the three interim
specs (D1); the playlists spec builds on this one.

The playlists folder is created here but nothing writes to it yet; that is the next spec.

## Rules every issue inherits

- **Placement.** The three tests in `CLAUDE.md`. The engine owns the store and the settings;
  the shell owns its own window geometry and column layout (not engine state).
- **Commands are total.** A library that cannot be opened is a notice and the app stays usable
  (empty queue, no store) until another is chosen.
- **Atomic writes** everywhere the store and settings already use them (`AtomicJson`); the
  `.corrupt` / `.recovered` recovery paths move with the files.
- **Verification.** `dotnet test` before every commit, with the counts in the message. Shell
  sessions run the Looks half of `docs/smoke.md`. The smoke doc gains a `--library=<path>` launch
  flag so the agent can run against a temporary library (issue 02).

## Order

| # | Issue | Blocked by |
|---|---|---|
| 01 | [A library on disk](issues/01-a-library-on-disk.md) | — |
| 02 | [Choose, remember and switch the library](issues/02-choose-remember-and-switch-the-library.md) | 01 |
| 03 | [Split the settings between machine and library](issues/03-split-the-settings-between-machine-and-library.md) | 01 |
| 04 | [Shuffle and repeat are remembered](issues/04-shuffle-and-repeat-are-remembered.md) | 03 |

## Out of scope

Migrating the AppData store (D13), restricting promised files to the library folder, more than
one library open at once, output device choice, and the playlists folder's contents.
