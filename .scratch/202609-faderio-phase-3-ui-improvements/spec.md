# Spec — faderio v3 phase 3: the playlists build's loose ends

Status: resolved
Blocked by: [playlists](../202609-faderio-phase-2-playlists/spec.md)

## Problem

The three interim specs are built. Building them left three loose ends, each recorded where it
was found but not fixed:

- With extra columns shown, the queue's ✎ button scrolls out of view
  ([playlists 06](../202609-faderio-phase-2-playlists/issues/06-configurable-columns.md)).
- A double-click in the transition manager on a row whose other track's file is gone opens the
  editor on a track of length 0. Every save there is refused as "past its end", which is not the
  reason ([playlists 05](../202609-faderio-phase-2-playlists/issues/05-the-transition-manager.md)).
- `AddTimeBudgetTests` fails about one full `dotnet test` run in three, under the load of the
  rest of the suite. Every commit message has to explain it away.

## Solution

Three independent issues, in any order. Phase 3's larger work (a library manager, a playlist
manager beyond the sidebar; see the v3 map's out-of-scope list) is not specified yet. It joins
this folder as its own issues once it has been grilled.

## Rules every issue inherits

- **Placement.** The three tests in `CLAUDE.md`. Whether a track's file is gone is the engine's
  to say; how the queue's columns fit is the shell's.
- **Verification.** `dotnet test` before every commit, with the counts in the message. Shell
  sessions run the Looks half of `docs/smoke.md` and add their items to it.

## Order

| # | Issue | Blocked by |
|---|---|---|
| 01 | [✎ stays in view with extra columns](issues/01-the-row-button-stays-in-view-with-extra-columns.md) | — |
| 02 | [A gone track opened from the manager](issues/02-a-gone-track-opened-from-the-manager.md) | — |
| 03 | [The add-time budget test holds under load](issues/03-the-add-time-budget-test-holds-under-load.md) | — |

## Out of scope

Stable track identity, finding a moved file again, cleaning a library's store of every
transition whose files are gone, and a pinned ✎ drawn outside the list.
