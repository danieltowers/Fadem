# 14: Editor: curve, from-end, revert, remove, validation

Status: resolved
Blocked by: —

Decided in [What completes the editor?](../../202609-faderio-phase-1-restage/issues/09-what-completes-the-editor.md) (From-end, Curve,
Revert and Remove, Validation). Carries v2 0009's and 0007's intent (stories 9, 12 and 27).
Closes audit finding 15.

## What to build

- **From-end** is a checkbox on the fade-out point only.
  - Ticking it converts the value, keeping the same moment.
  - Capture into a from-end field stores `Length − playhead`.
  - The engine computes both.
  - The resolver's handling of a from-end fade-in stays as it is; the editor never writes one.
- **Curve** is equal-power or linear, one per transition.
- **Revert** reloads the fields from the store.
- **Remove transition** calls `Unauthor` and leaves the values as an unsaved draft, so Save
  restores them.
- **Saving with every field blank calls `Unauthor`.** The store never holds an empty
  transition.
- **`Author` validation.** It refuses impossible values and returns them with reasons, rather
  than throwing. The editor shows each reason next to its field. The impossible values are:
  - a fade-out point past the outgoing track's `Length`;
  - a fade-in point past the incoming track's `Length`;
  - a from-end point longer than its track;
  - a negative duration.

  A duration that merely runs past `Length` is still shortened silently.

## Where the logic lives

- **Engine:** the from-end conversion and capture, validation, and `Unauthor`. The shell
  never subtracts from `Length` (placement test 2).
- **Shell:** the controls, and the per-field display of the returned reasons.

## Acceptance

- [x] Ticking from-end on 3:12 of a 3:40 track shows 0:28, and the resolved fade starts at the
      same moment.
- [x] Capture with from-end on stores the distance to the end.
- [x] Linear, saved, plays linear, and the store round-trips it.
- [x] Remove transition leaves the fields filled; Save puts the transition back.
- [x] A blank save leaves no entry in `transitions.json`.
- [x] Each impossible value is refused with its reason shown beside the field, and nothing is
      stored.

## Tests

- **fake:** conversion, capture, validation and blank-save.
- **unit:** the resolver where it is touched.
- **form:** controls, Revert, Remove and reasons.

## Smoke

- **Looks:** the curve and from-end controls fit without scrolling. Refusal reasons are
  readable beside their fields.
- **Sounds:** a from-end fade-out captured on a drum hit lands on the hit.
