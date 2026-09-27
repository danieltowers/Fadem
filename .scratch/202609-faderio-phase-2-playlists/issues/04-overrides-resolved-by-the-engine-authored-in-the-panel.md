# 04: Overrides: resolved by the engine, authored in the panel

Status: resolved
Blocked by: 01

Decided in [D21, D22, D23](../../202609-faderio-phase-2-interim-improvements/decisions.md), ADR 0005.
Glossary: **Transition override**.

## What happens today

`ITransitionStore` has `GetOverride / UpsertOverride / RemoveOverride(playlistName, …)` and
`TransitionResolver.Resolve(global, override, …)` merges per parameter — but
`PlaybackEngineBoundary.cs` and `PlaybackEnginePreview.cs` always pass `null`, and no UI
touches overrides.

## What to build

- **Resolution:** wherever the engine resolves an adjacency (boundary scheduling, preview,
  `CanTransitionNow`, the join in issue 01) it passes the open playlist's override for that
  pair. An override with no global transition resolves against the unauthored fallback
  (hard cut or default crossfade per settings) — a **playlist-only** transition (D22).
- **Authoring surface:** `Transition(Adjacency)` returns both layers: `Global` (spec or null)
  and `Override` (partial spec or null) and the `Effective` result. `AuthorOverride(Adjacency,
  partial)` and `UnauthorOverride(Adjacency)` join `Author` / `Unauthor`, returning the same
  refusal shape, validated against the *effective* transition (a fade cannot exceed the
  length, etc.). `IsAuthored` gains the layer.
- **Panel:** a two-way selector at the top, "Editing: ● Global ○ This playlist (*name*)".
  - Global mode: unchanged.
  - Playlist mode: each box shows the effective global value as grey placeholder text; typing
    makes that parameter part of the override; clearing a box drops it from the override. The
    curve combo gets a blank "(global)" entry. Capture and Nudge write into the override.
    **Save** → `AuthorOverride`; **Revert** reloads; **Remove override** → `UnauthorOverride`
    (replaces "Remove transition" in this mode). Preview plays the drafted effective result.
  - The mode is remembered for the session (D23): the row button and the menu open in the
    last mode. Switching modes with unsaved edits prompts Save/Discard/Cancel.
  - A heading line states the effective values when the two layers differ.

## Where the logic lives

- **Engine:** the resolver wiring, `Transition`'s layers, the two override commands and their
  validation against the effective transition.
- **Shell:** the selector, placeholders, the mode memory.

## Acceptance

- [x] Author A→B globally at 10 s; in playlist P override the fade-out point to 8 s; play P →
      the fade begins at 8 s; play A→B in playlist Q → 10 s.
- [x] Override only the fade-in duration; the other three fall through and follow later edits
      to the global.
- [x] No global for C→D; override all four in P → P plays it; Q plays the unauthored fallback.
- [x] Rename P → R: the override follows (issue 01).
- [x] Remove override → P plays the global again.

## Tests

- **fake output:** each acceptance line renders through the chain with the right fade start.
- **unit:** validation against the effective transition; a partial override serialises only
  the set parameters.
- **form:** placeholder rendering, save/remove in each mode, mode memory.

## Smoke

- **Looks:** switch the selector to This playlist; the boxes show grey global values; type one;
  Save enables and "Remove override" is shown.

## Comments

- Built 2026-09-27. **Engine:** `PlaybackEngineAuthoring.cs` gains `AuthorOverride(adjacency,
  partial)` and `UnauthorOverride(adjacency)`, which write the open playlist's override (no
  playlist argument: the engine knows which is open). `Transition(adjacency)` now returns
  `TransitionLayers(Global, Override, Effective)` with `Differ`, new in
  `Domain/TransitionLayers.cs`; `Effective` is the merged spec, the resolver's own `Merge`, now
  internal. `IsAuthored` and `Preview` take a `TransitionLayer` (default `Global`). The resolver
  gets the open playlist's override at the boundary (so the join too), at Transition now, and in
  the preview; `CanTransitionNow` resolves nothing, so it needed no change. The rows' ✎ marks count
  the override, so a playlist-only transition is bold in its playlist alone, and the marks are
  cleared when another playlist opens.
- **Remove override sits beside Remove transition, it does not replace it.** The issue says
  "replaces", D21 says "beside", and the decisions file wins. In playlist mode Remove transition
  still removes the global transition, and the placeholders then show that.
- **Validation.** An override is refused as `Author` refuses, against the global with the
  override laid over it. A partial that sets nothing removes the override, and so does a curve
  alone with no global, since a curve shapes fades nobody placed (the build issue 14 rule). A
  curve alone over a global is kept.
- **Preview in each mode.** Playlist mode plays the drafted override over the stored global. Global
  mode plays the global draft under the stored override, as the playlist would: the issue asks for
  the override wherever the engine resolves. The heading line says when the two differ.
- **Nudge and From end on an empty box** in playlist mode start from the global point its
  placeholder shows, and write the result into the override, since "Nudge writes into the
  override". From end follows the global point's end while the override sets no point.
- **Not done: Render.** `RenderBoundary` renders on a fresh engine with no playlist open, so it
  still renders the global transition. The issue does not list it; say if it should render the
  effective one.
- **Layout.** At the default window size the selector and the heading line pushed Render below the
  fold. The title is 26 px high (was 44), button rows 38 (was 40), and the preview line takes room
  only while a preview runs. Render and Remove override share a row. Global mode's layout is
  otherwise as before.
- **"Save enables"** in the smoke item: Save is enabled whenever the editor is open, in both
  modes, as before. Nothing was changed to disable it with no edits.
- Tests: `PlaylistOverridesTests` (18, fake output): each acceptance line through the chain, read
  by where the output first leaves the outgoing DC level (P at 1.5 s, Q at 2 s; a playlist-only
  transition in P, a hard cut or the default crossfade in Q; rename; remove), a pending boundary
  rescheduled, Transition now, preview in each layer, the row mark, the layers and `Differ`,
  validation against the effective transition, a partial that sets nothing, a curve alone, the
  JSON holding only the set parameter, `IsAuthored` per layer, and no library. Temporarily
  dropping the override at the boundary failed 5 of them, and at Transition now and the preview
  3 more. Form tests (8): placeholders, "(global)" and Save as override with the heading line;
  clearing a box drops it and a curve overrides; Remove override; global mode unchanged; the mode
  remembered on the next adjacency; Cancel and Save on a mode switch; a nudge from the
  placeholder and a preview. Presenter: the heading line and "(global)". Existing tests read
  `Transition(…).Global`.
- dotnet test: 1002 passed, 0 failed (Core 789, App 213).
- Looks smoke, fresh temp library and machine folders, two generated tones with `--edit-first`:
  the editor reads "Editing: ● Global ○ This playlist (Untitled)" under the heading, with all
  four fields, Save / Revert / Remove transition and Render in view at the default size. A global
  1.5 / 0.5 / 0 / 0.5 saved ("Saved Outgoing tone → Incoming tone."). Switched to This playlist:
  the four boxes empty (UIA reads '' in each) with 1.5, 0.5, 0, 0.5 in grey, the curve
  "(global)", Remove override beside Render and under Remove transition, all in view. Typing 1 in
  the fade-out point shows it black among the grey; Save said "Saved Outgoing tone → Incoming
  tone in Untitled.", `transitions.json` holds the override with only `FadeOutPoint`, and the
  grey line "Untitled plays: fade-out at 1 s for 0.5 s, fade-in from 0 s over 0.5 s,
  equal-power." appears, the last button row still in view. Remove override said "Removed the
  override of … in Untitled.", emptied `playlistOverrides`, kept "1" as a draft and dropped the
  line. Preview in playlist mode ran ("Tail · incoming 0:00" two seconds in, as a 1 s fade-out
  point gives) with the preview line taking its row only while shown. Revert, then close: no
  prompt. Not smoked in the app: the Save / Discard / Cancel prompt on a mode switch and the mode
  kept for the next ✎ (form tests cover both).
