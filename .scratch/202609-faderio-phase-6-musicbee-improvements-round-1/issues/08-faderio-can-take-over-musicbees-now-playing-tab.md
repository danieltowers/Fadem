# 08: faderio can take over MusicBee's Now Playing tab

Status: resolved
Blocked by: none

Phase 6, improvements round 1, the MusicBee host only. Raised 2026-10-02: "can faderio
optionally be a locked tab so it loads in the center frame of musicbee?", then "would it be
possible to overwrite the now_playing view? i never use it".

## What to build

An opt-in setting in faderio's **Configure…** dialog: **Show faderio in the Now Playing tab**.
While it is on, MusicBee's **Now Playing** tab shows a faderio view filling the whole centre
(its own Now Playing content hidden), and every other tab is untouched. Off, the plugin behaves
exactly as now. The sidebar panel, wherever Arrange Panels put it, keeps working either way.

## What the spikes found (2026-10-02, portable MusicBee 3.6.9403)

Two throwaway plugins, since removed. The findings are recorded in the agent memory
`musicbee-addpanel-mainpanel-spike`.

- **No supported route.** The plugin API has no tab of a plugin's own and no tab-changed
  notification. The Now Playing tab's layout dialog offers only MusicBee's own elements.
  `MB_AddPanel(control, PluginPanelDock.MainPanel)` places a fixed overlay over every library tab
  that is never resized with the window. Rejected.
- **Embedding works.** MusicBee's main form has only a few WinForms children and paints the rest.
  A tab switch *swaps* the form's children. On Now Playing exactly one child is visible: a
  container whose type is nested in the form's type, covering the centre and the sidebar. A
  control added to it with `Dock = Fill` covered the tab, and MusicBee's layout resized it with
  the window (958 → 1384 px and back). Library tabs were unaffected.
- **MusicBee rebuilds the view on every visit** and disposes the old one, with its children, on
  leaving. In spike 2, a control that must survive was taken out synchronously in the form's
  `ControlRemoved` handler and put back in the new view after the change settled.
- **On the Now Playing tab, the sidebar's faderio panel is detached** from the form, so only one
  faderio view is on screen at a time.
- MusicBee's biography scroll strip drew over the left edge; its own Now Playing children need
  hiding.

## Proposed design (to confirm at triage)

- **A fresh view per visit, no rescue.** `Host` already gives each panel MusicBee makes its own
  `PluginView` (`Host.AttachView`), and drops it when MusicBee disposes the panel. The Now
  Playing view is treated the same way: on each visit, `AttachView` into the new container,
  docked to fill, with MusicBee's own children hidden. MusicBee disposes it on leaving, as it
  disposes panels now. The engine, the queue and the notice log live in `Host`, so nothing is
  lost but the view's own state (see open question 2).
- **Recognition** is a small class in `Faderio.MusicBee`, `NowPlayingTakeover` or similar. It
  hooks the main form's `ControlAdded` / `ControlRemoved` and checks once the change settles
  (`BeginInvoke`): exactly one visible child, of a type nested in the form's type. Anything else
  is "not Now Playing", and it does nothing.
- **It fails safe.** If the form cannot be found, or the rule never matches after a Now Playing
  visit, faderio leaves MusicBee alone. A message in the sidebar view's strip says so once, never
  in a message box. MusicBee's own Now Playing view is never removed or disposed by faderio,
  only covered and its children hidden. So turning the setting off, or the next visit, gives it
  back.
- **Turning it on or off** takes effect at the next Now Playing visit, with no restart.
- **Tests** (`Faderio.MusicBee.Tests`, net48): the recognition rule against a fake form whose
  children are swapped as MusicBee swaps them (a nested container type, a library layout of
  several children, a hidden leftover); a view attached on each visit and dropped on dispose;
  nothing attached with the setting off; nothing attached, and one notice, when the rule does not
  match. The **MusicBee Looks** half gains an item: with the setting on, Now Playing shows
  faderio full-centre, resizes with the window, the library tabs are unchanged, and with it off
  the Now Playing tab is MusicBee's own again.

## Open questions for triage

1. **Where the setting lives.** `MachineSettings` (Core) is shared with standalone faderio, and
   this setting means nothing there. Should it be a plugin-only field in the plugin's
   `settings.json`, or a Core field the App ignores? Proposed: plugin-only.
2. **What a revisit shows.** A fresh view starts with the editor closed and the queue scrolled
   to the top. Is that acceptable, or should the open transition reopen on the next visit? That
   would need a place to remember it outside the view (CLAUDE.md test 3 is about engine state,
   not this, but it is still state).
3. **Its layout.** The Now Playing area is about 1500 px wide maximised. Should the view lay out
   as now (queue with the editor docked beside it), or should the editor get a wider layout
   there?
4. **The version guard.** The recognition rule rests on MusicBee's internals. Should faderio
   also refuse to try on a MusicBee version other than the one it was checked against, or rely
   on the rule failing safe? Proposed: rely on the rule, plus the one-time notice.
5. **ADR.** This is the first time faderio reaches into MusicBee's own controls. An ADR (or an
   amendment to ADR 0012) should record why, and the fail-safe rule.

## Acceptance criteria

- [x] With the setting on, MusicBee's Now Playing tab shows faderio filling the centre, and
      resizes with the window.
- [x] Every other tab, and the sidebar panel, are unchanged.
- [x] With the setting off, or when faderio cannot recognise the view, MusicBee behaves exactly
      as without this issue, and in the second case says why once.
- [x] Repeated tab switches leak no views and leave no disposed controls behind.

## Comments

**2026-10-02, triage answers (the user):**

1. The setting is plugin-only: `plugin.json` beside the plugin's `settings.json`.
2. A revisit reopens the transition the editor had open, if the same library is still open.
3. The layout is as now: queue with the editor docked beside it.
4. No version guard: the rule fails safe, with the one-time notice.
5. A new ADR: `docs/adr/0013-faderio-may-cover-musicbees-now-playing-view.md`.

The "rule does not match" case is a single visible child of a type not nested in the form's:
the library layout (several children) is not reported, since it is not a Now Playing visit.
