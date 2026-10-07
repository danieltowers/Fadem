# 07: Section groups

Status: resolved
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

- [x] Looks, both hosts: in the "test playlist" playlist, "intro" and "foo" read in the palette with
      their swatches. Collapse and expand still work by a double-click on the header.
- [x] The existing section and group tests pass unchanged.
- [x] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments

2026-10-07, the finding, before building. A throwaway probe put a themed, owner-drawn queue
with two sections on an off-screen window and captured it on both runtimes.
- **`LVM_SETGROUPMETRICS` does nothing.** The list takes `crHeader` and reads it back, but with
  visual styles on (as both hosts are) the header is drawn in the theme's own blue regardless.
- **Custom draw reaches the header.** The list sends `NM_CUSTOMDRAW` (reflected to the list by
  WinForms) at `CDDS_PREPAINT` with `dwItemType = LVCDI_GROUP`, `dwItemSpec` the group's native
  id and `rcText` the header's rectangle. Answering `CDRF_SKIPDEFAULT` there leaves the header
  to us, on net8.0 and net48 x86 alike. WinForms' own owner drawing never sees the group stage.
- The unheaded group above the first section gets a header rectangle too; native draws it
  empty. Natively the chevron points down while expanded and right while collapsed.
- Hit-testing stays native, so the chevron, the double-click and the keys are untouched by
  drawing the header ourselves.

2026-10-07, built.
- **`StudioTheme`'s `GroupHeaders`**, a hook on every themed list, answers the group stage
  itself: the header on the list's ground, the title image (a section's swatch, through
  `ImageList_Draw` from the list's own header images), the name in small bold capitals (the
  queue header's 7.5 pt bold) in Text, an Edge rule to the chevron, and the chevron in Dim, down
  while expanded and right while collapsed, as Windows'. The unheaded group stays blank, and an
  uncollapsible group has no chevron. A focused header gets a Dim focus rectangle.
- `ListGroups` gains `WithId` (the group custom draw names), `HeaderBounds` (which `HeaderAt`
  now uses) and `TitleImages`.
- **Tests**, `SectionGroupThemeTests`, on screen like the rename box's (a shown window, off
  screen, never activated), on both runtimes: an expanded and a collapsed header each show the
  swatch, the Dim chevron, and Text as their main ink. ClearType's fringes come too near Windows'
  header blue (`#1B3276` on Duophonic's Well) to look for the blue pixel by pixel, so the test
  checks the most common ink instead. Both tests fail with the hook off. The probe was thrown
  away; it measured Windows, not the engine.
- On Duophonic the rule barely shows: Edge (`#0F0F0F`) is close to the Well (`#121212`), as the
  palette defines it. It shows plainly in the built-in palette.

Looks:
- **App**, a scratch library holding a copy of "test playlist", in Duophonic's `palette.json` and in
  the built-in palette: INTRO and FOO read in the palette with their grey swatches (both are
  colour 7) and dim chevrons. A posted double-click on FOO collapsed it (chevron right,
  `collapsed=1` in the copy) and another expanded it.
- **MusicBee**, the installed one in Duophonic, on its "library" with S: connected, faderio's
  library pointed at the user's music library for the run: the same. Collapse and expand wrote `collapsed=1` and
  took it out again, and the real "test playlist" ended byte for byte as backed up. faderio's
  plugin settings were put back and MusicBee closed with nothing new in its error log. This
  build of the plugin stays installed.

**Found, not fixed:** a section's rows show no stripe of its colour at their left. The queue is
owner drawn (`QueueRowStyle.Draw`), and its cells draw text only, never the item's image, so the
stripes `QueueSections` sets (`ImageIndex`) are never painted. The phase 6 Sections smoke item
still expects them. Not touched here: the group headers were this issue's scope.
