# 05: The transition editor

Status: ready-for-agent
Blocked by: 02

Shell. See the [spec](../spec.md). This issue makes sure the editor panel reads well in the theme
once issue 02 has themed its controls.

## What to build

- **`TransitionEditorPanel`**:
  - The fields sit in a Well with Edge borders.
  - The radio buttons and check boxes (Global / This playlist, From end) are in Text, or Dim
    when disabled, on the panel's own ground, never a lighter box behind them.
  - The heading is in Text and the hint lines are in Dim.
  - The −/+ and −1/+1 nudges are small keys.
  - The fade curve dropdown is drawn on Well.
- **The traffic light** keeps its green, amber and red meaning. Its ring and its off state come
  from the palette.
- **The editor's scroll area** uses the native dark scroll bar.

## Acceptance

- [ ] Headless: no control in the editor keeps a `SystemColors` colour, and no check box or radio
      button has a back colour different from its parent's.
- [ ] Looks, both hosts, with a transition open and with none: every label is readable, the
      disabled state is visibly disabled but legible, and the traffic light is clear.
- [ ] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments
