# 07: Section groups

Status: ready-for-agent
Blocked by: 03

Shell. See the [spec](../spec.md), "Section groups".

## What to build

- The queue's section group headers (`ListGroups`, phase 6 issue 09) are drawn in the palette,
  not Windows blue:
  - the name in small bold capitals in Text;
  - the colour swatch in the section's own `SectionPalette` colour;
  - the chevron and rule in Dim/Edge.

  Find out first what the native group header lets us colour (`LVM_SETGROUPMETRICS` text
  colours, or owner-drawn headers through `NM_CUSTOMDRAW` at `LVCDI_GROUP`). Record the finding
  here before building.
- Collapsing and expanding, the double-click on a header, and the group view's keys are unchanged.

## Acceptance

- [ ] Looks, both hosts: in the "test playlist" playlist, "intro" and "foo" read in the palette with
      their swatches. Collapse and expand still work by a double-click on the header.
- [ ] The existing section and group tests pass unchanged.
- [ ] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments
