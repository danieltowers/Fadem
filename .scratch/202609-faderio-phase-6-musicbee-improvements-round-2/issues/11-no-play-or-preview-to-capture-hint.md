# 11: No "Play … or preview to capture" hint

Status: resolved
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: ""Play {TitleOf(track)} or preview
to capture" messagees are not helpful. hide them, we may revisit".

## What to build

The editor shows a hint beside each Capture button while it is greyed out
(`TransitionEditorPanel._captureOutHint`, `_captureInHint`, set from `Presenter.FmtCaptureHint`).
Hide both hints. Keep the code that sets them, with the labels not visible, so they can come back;
the Capture buttons' own tooltips ("Capture *b*'s playhead") stay.

## Acceptance

- [ ] No "Play … or preview to capture" text in the editor, in either host, while Capture is greyed
      or live; no empty gap where it was.
- [ ] Capture's tooltip unchanged.
- [ ] Tests that read the hint's text are updated to check it is hidden; smoke items that name the
      hint updated.

## Comments

### 2026-10-09, built

The hints are set as before but shown only with `CaptureHints`, false. Capture's tooltip unchanged.
