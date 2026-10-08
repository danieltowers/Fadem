# 06: Dialogs and menus

Status: resolved
Blocked by: 02

Shell and both hosts. See the [spec](../spec.md), "Every window and dialog" and "Menus".

## What to build

- **Every faderio window** gets `StudioTheme.Apply` with its host's palette, title bar included:
  - the App's Settings;
  - the transition manager (its grid in the palette: Well cells, Raised selection, capital
    headers);
  - the library chooser;
  - the notice list;
  - faderio's MusicBee Configure dialog;
  - the section name dialog;
  - the playlist rename box.
- **`ContextMenuStrip`s** use a `ToolStripRenderer` in the palette: Chrome ground, Raised
  highlight, Edge separators, Text and Dim items. This covers the queue's menu, the header's
  column menu, the section menu, the edit transition menu and the playlist menu.
- **The status strip** is in Chrome with Dim text. Its link ("5 notices") is in Accent.
- **Message boxes** stay the system's. Windows draws them.

## Acceptance

- [x] Headless: each dialog listed, made with a palette, has no `SystemColors` back or fore colour
      left. Each menu's renderer is the palette's.
- [x] Looks, both hosts (App 2026-10-07; MusicBee 2026-10-08, see Comments): each dialog and each right-click menu, by screenshot, with no white or
      system-grey surface.
- [x] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments

2026-10-07, built. Left open for the MusicBee look only.
- **The menu renderer**, `Theme/StudioRenderer`: a professional renderer whose colour table is
  all palette roles. The ground is Chrome, the item under the pointer Raised, separators and the
  border Edge. Items are Text and disabled ones Dim. Ticks are in the accent; an item with its
  own image (a section's swatch) is framed in the accent instead. The submenu arrow is Text or
  Dim. `StudioTheme` gives one to every strip it themes. A control's `ContextMenuStrip` is
  themed with the control, and again when it changes. A submenu takes its owner's renderer
  (a test checks this). The header's column menu is shown by itself, so each host applies the
  theme to it.
- **The status strip** has the same renderer. Its text is Dim and its link the accent.
- **Each dialog takes the host's palette** as a constructor argument (built-in by default) and
  themes itself: `SettingsForm`, `SettingsDialog`, `LibraryChooser` (and `Choose`),
  `NoticeListForm`, `TransitionManagerForm`, and `Dialogs.SectionNameForm` (split out of
  `AskSectionName` so a test can build it unshown). The App reads `palette.json` before the
  launch chooser, so that chooser is themed too. The plugin's Configure reads the skin afresh.
  The chooser's and the notice list's lists are drawn as the queue is (Raised selection, small
  capital headings), and a gone library's cells are Dim.
- **Hints in system grey** (`SystemColors.GrayText`) are now `Ink.Dim`: the Settings hints, the
  chooser's hint and the manager's legend. A gone row in the manager, and a gone track in its
  picker, are Dim. The picker draws itself on the Well, so the theme leaves any combo that is
  already owner-drawn alone.
- **The grid** (`DataGridView`): cells on the Well in Text, the selection on Raised, horizontal
  Edge rules, and headings drawn as the queue's header draws them. Preview and Preview global
  are drawn as keys. Their flat cells barely read as buttons, so the theme paints them.
- **Group boxes**: an Edge frame, and the title as a section label (small bold capitals in the
  accent). **Spin boxes**: on the Well with an Edge frame. Their arrows are redrawn on the Well
  in Text, or Dim while disabled; WinForms drew them light grey.
- **Lists** are framed in Edge like text boxes. Before, the dark list theme's frame was a light
  `#828790`; this changes the queue's and playlists' frames too.
- **Scroll bars**: a multi-line text box (the notice list's detail) and a grid's scroll bars use
  the dark theme too.
- **The rename box** is the edit control the list makes. It asks the list for its colours
  (`WM_CTLCOLOREDIT`), so the list's hook answers Text on the Well. **Known gap: its one-pixel
  frame stays Windows' grey (`#646464`).** Drawing over it in the box's own paint held only when
  its test ran alone; with other test classes running it never showed in the capture, on either
  runtime. Clearing the box's border style left the frame as it was, so something else (the
  list, I believe) draws it. Cause not found. An on-screen test (a shown window, off screen and
  never activated) checks the ground and text on both runtimes. It clears the selection first:
  the box opens with the name selected in the system's blue, as any text box does.
- Message boxes stay Windows' own, as the issue says.

Looks (App), by screenshot in Duophonic's `palette.json` and in the built-in palette:
- Settings, the transition manager (with two pre-written transitions, one to a gone file), the
  library chooser, the notice list (opened from the "1 notice" link, in the accent) and the
  section name dialog (row menu, Section, Start a section here…) are all in the palette, with
  dark title bars and no white or system-grey surface.
- The queue's, header's and playlist's menus, and the Section submenu, are on the chrome:
  disabled items dim, ticks in the accent.
- Menus are layered popups, which `PrintWindow` draws black, so they were read off the screen.
- The rename box was not seen in the App: it needs real keyboard focus, which a smoke run must
  not take. The on-screen test covers its ground and text; its frame is the known gap above.

Looks (MusicBee): **not done**. The plugin build was installed, but S: was offline, so MusicBee's
library prompt showed the user's "library" as not located, and clicking it opened a browse
window. MusicBee was quit from its prompt rather than opened on another library, and left closed
as it was found. To finish: reconnect S:, then check Configure…, its Choose…, the manager, the
notice list, the section name dialog and the three menus in Duophonic.

Tests: net8.0 Core 1152, Shell 202, App 192; net48 x86 Core 1131, Shell 202, MusicBee 106; 0 failed.

2026-10-08, the MusicBee look, done. The installed MusicBee in Duophonic, opened on the user's
"library" with S: connected (MusicBee's own library settings were rewritten at close, "temp"
untouched). faderio's library was pointed for the run at a scratch one (`libmb06`): a playlist
"Theme check" of two silent WAVs and one missing file, and the two pre-written transitions
(one to a gone file). The plugin's `settings.json` was backed up first and restored after;
MusicBee closed with nothing new in its error log. By screenshot:
- **Configure's dialog**, opened from the view's ⚙, which runs the same `Host.Configure` as
  Preferences' Configure… (same dialog, the skin read afresh): dark title bar, fields and
  dropdowns on the Well, the tick and radio in the accent, hints dim, OK, Cancel and Choose…
  as keys. **Its Choose…**: the chooser on the Well, its open row raised, small dim capital
  headings, the hint dim, Remove from list a dimmed key. Both cancelled.
- **The transition manager** (queue menu, Transitions…): capital headings, cells on the Well,
  the legend dim, the gone row dim, Preview and Preview global as keys, dark title bar.
- **The notice list**, from the strip's "1 notice" in the accent (opening the playlist refused
  the missing file): small capital headings, the row raised, the detail on the Well with a dark
  scroll bar. The status text is dim.
- **The section name dialog** (Section → Start a section here…, cancelled; the playlist file
  unchanged): dark title bar, the box on the Well, keys.
- **The menus**: the queue's, the header's (ticks in the accent, the locked # dim), the
  playlist's and the Section submenu are on the chrome, disabled items dim.

Seen, then fixed the same day at the user's word:
- **The playlist list's selection was Windows' blue (`#0E2A41`) while the list had focus**, as
  after a click on a playlist. The list was drawn by Windows, and its dark theme draws the
  selection from the row's state: clearing the selected state in custom draw left it blue (and
  tried first, it lit the wrong row). `PlaylistSidebar` now has its list drawn by
  `QueueRowStyle.Draw`, as the queue, the chooser and the notice list are, keeping the list's own
  row height (`ownRowHeight`). The selected row is Raised, the open one bold in the accent.
- **The manager's current cell had a light, dotted focus frame** (`#EDEDED`, round "→" in row
  1). The theme now paints a grid's data cells without WinForms' focus frame; the selected row
  on Raised says where the current one is.
- Tests (`SelectionThemeTests`, on screen with focus cues on): a focused playlist's selected
  row is Raised and not the theme's blue; the playlists keep their own row height; a focused
  grid's empty current cell has nothing light in it. Each failed before its fix (the grid's on
  net48 x86 only: on net8.0 the grid drew no focus cue in the test).
- Looks, after a real click into each, in the App (Duophonic `palette.json`) and in MusicBee
  (Duophonic, `libmb06` as above, settings restored): the playlist row Raised (`#3D3D3D`), the
  manager's current cell unframed.
