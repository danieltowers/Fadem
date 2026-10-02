# faderio may cover MusicBee's Now Playing view, recognised by a rule that fails safe

Status: accepted (2026-10-02, phase 6 issue 08)

The user asked for faderio in MusicBee's centre, and said they never use the Now Playing tab. No
plugin API gives faderio a tab or says which tab is shown. So, opted into in faderio's Configure,
the plugin watches MusicBee's main form and puts a faderio view over the Now Playing view. This
is the first time faderio reaches into MusicBee's own controls rather than those MusicBee lends
it (ADR 0012's panel and menus).

Two spikes on the portable MusicBee 3.6.9403 found what this rests on. MusicBee's main form has
only a few WinForms children and paints the rest. A tab switch swaps those children. On Now
Playing exactly one is visible: a container whose type is nested in the form's type, built
afresh on each visit and disposed, with its children, on leaving. A control docked to fill it is
sized by MusicBee's layout.

## Decision

- **The rule.** Once a change to the form's children has settled (`BeginInvoke`), exactly one
  visible child, of a type whose declaring type is the form's, is the Now Playing view. Anything
  else is not, and is left alone.
- **A fresh view per visit, no rescue.** Each visit gets its own `PluginView`, attached as a
  panel's is (`Host.AttachView`). MusicBee disposes it with its container, and `Host` drops it,
  as it does a panel's. The engine, queue and notice log live in `Host`. Only the editor's open
  transition is carried over: it is saved and remembered in the form's `ControlRemoved` handler,
  which runs before MusicBee disposes the container, and reopened on the next visit if the same
  library is still open.
- **It fails safe.** MusicBee's own view is covered and its children hidden, never removed or
  disposed, so turning the setting off, or the next visit, gives it back. When the form cannot
  be found, or a single visible view is not of a nested type (another MusicBee build), faderio
  leaves MusicBee alone and says so once a session, in the strips and the notice list, never
  in a box.
- **No version guard.** faderio does not refuse to run on other MusicBee builds. The rule is the
  guard: obfuscated names change between builds, but the rule names none.
- **The setting is the plugin's own**, in `plugin.json` beside the plugin's `settings.json`, not
  in Core's `MachineSettings`, which standalone faderio shares and where it would mean nothing.
  It takes effect at the next visit to the tab, with no restart.

## Considered options

**`MB_AddPanel(control, PluginPanelDock.MainPanel)`.** Rejected. It places a fixed overlay over
every library tab, which is never resized with the window and has no API to ask the sidebar's
width.

**Rescue one view across visits** (take it out in `ControlRemoved`, put it back after the
change). It worked in spike 2. Rejected: a fresh view per visit is what `Host` already does for
panels, and keeps no control alive outside MusicBee's tree.

**A version guard** (refuse on any build but 3.6.9403). Rejected: it would turn the feature off
on every update, even where the rule still holds.

## Consequences

- A MusicBee update that changes how Now Playing is built makes the setting do nothing, with one
  notice, until the rule is updated.
- On the Now Playing tab, MusicBee detaches the sidebar's faderio panel, so one faderio view is
  on screen at a time.
- MusicBee shows some of its view's children later: the biography's scroll strip appears once
  its text loads, and covered faderio's left edge in the first smoke run. So faderio keeps every
  child of the covered view hidden, including ones added after it, until the tab is left.
- MusicBee still paints its "Now Playing" and "Playing Tracks" captions in the strip above the
  container, which is outside it.
