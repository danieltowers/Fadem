# Spec — faderio v3: interim transport fixes

Status: ready-for-agent
Blocked by: —

## Problem

The daily driver works, but a handful of transport gestures fight the user: Play after a pause
ignores the row they just clicked, the attended row loses its marker when nothing sounds, the
seek bar only moves by dragging its thumb, there is no way to jump a few seconds, and nudging a
point by a tenth of a second means retyping it. The editor is reached only by right-click.

## Solution

Seven small vertical slices, none touching storage. Every decision is in
[decisions.md](../202609-faderio-interim-improvements/decisions.md) (D3–D9); **when an issue
and that file disagree, the decisions file wins.** First of the three interim specs (D1).

## Rules every issue inherits

- **Placement.** The three tests in `CLAUDE.md`. Each issue says where its logic lives. New
  engine members follow `codebase-design`.
- **Commands are total.** No engine command throws for a user-reachable condition; a refusal is
  a notice.
- **Rows, not tracks.** Commands that act on the queue take `RowId`s.
- **Verification.** `dotnet test` before every commit, with the counts in the message. Every
  issue here touches the shell, so every session runs the Looks half of `docs/smoke.md` and
  reports item by item. The Sounds half is the user's. Each issue adds or amends its smoke
  items; never let one go stale.

## Order

| # | Issue | Blocked by |
|---|---|---|
| 01 | [Hide the stop button](issues/01-hide-the-stop-button.md) | — |
| 02 | [Play plays the selected row](issues/02-play-plays-the-selected-row.md) | — |
| 03 | [The attended-row marker in every state](issues/03-the-attended-row-marker-in-every-state.md) | 01 |
| 04 | [Click anywhere on the seek bar](issues/04-click-anywhere-on-the-seek-bar.md) | — |
| 05 | [Jump five seconds either way](issues/05-jump-five-seconds-either-way.md) | 04 |
| 06 | [Nudge a point by a tenth of a second](issues/06-nudge-a-point-by-a-tenth-of-a-second.md) | — |
| 07 | [A row button opens the editor](issues/07-a-row-button-opens-the-editor.md) | — |

## Out of scope

A resting playhead while stopped (D3), nudging durations (D8), live scrubbing (D6), keyboard
shortcuts for transport, and anything that needs a library or an open playlist — those are the
next two specs.
