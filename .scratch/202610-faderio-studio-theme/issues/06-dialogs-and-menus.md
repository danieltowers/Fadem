# 06: Dialogs and menus

Status: ready-for-agent
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

- [ ] Headless: each dialog listed, made with a palette, has no `SystemColors` back or fore colour
      left. Each menu's renderer is the palette's.
- [ ] Looks, both hosts: each dialog and each right-click menu, by screenshot, with no white or
      system-grey surface.
- [ ] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments
