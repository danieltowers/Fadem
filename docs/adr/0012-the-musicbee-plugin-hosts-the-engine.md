# The MusicBee plugin hosts faderio's engine; MusicBee's player never plays a faderio playlist

Status: proposed (accepted when the phase 5 spike passes)

faderio gains a second host: a MusicBee plugin that authors, previews and plays transitions
inside MusicBee. MusicBee's player cannot play an authored transition, so the plugin plays with
`Faderio.Core` itself. It runs in MusicBee's 32-bit .NET Framework process, with its own audio
output. Core multi-targets `net8.0;net48`, and its promise tests run on both. Starting playback
in the plugin stops MusicBee's player; a faderio playlist is only ever played by the plugin.

This reverses the v3 map's out-of-scope line "MusicBee plugin interop" only in part. That line
was about the old plugin, which drove MusicBee's player. The reason behind it, "the engine has
to be ours", still holds: here the engine is ours, running in someone else's process.

Decided in the phase 5 grilling
([brief](../../.scratch/202609-faderio-phase-5-musicbee/brief.md)).

## Considered options

**Drive MusicBee's player, as the old plugin did.** Rejected. The API has one global crossfade
switch, no duration or curve, millisecond seeking and asynchronous track-change notifications,
with no DSP or output hook. The old `mb_DummyTrackController` polled every 250 ms and seeked to
trigger MusicBee's crossfade. It could skip and cut, but not mix.

**Keep faderio as the player and make the plugin a remote.** Rejected by the user. The engine
and the promise would stay untouched, but the sound would come from a separate process
rather than from MusicBee.

**Pre-render each transition and serve the segments through a Storage plugin.** Rejected.
MusicBee's player would play it, but rows that are not tracks would come back (ADR 0004), and
every edit would need a re-render.

**A second engine on MusicBee's BASS** (`Player_OpenStreamHandle` plus bassmix). Rejected.
The promise would then have to be proven on two engines.

**Stop here.** The fallback if the spike fails.

## Consequences

- Every Core change must compile on net48. `dotnet test` runs both targets, and commit messages
  state both counts.
- Core cannot use `File.Move(…, overwrite)`, `EnumerationOptions`, `[GeneratedRegex]` and other
  net5+ APIs without a net48 path. The engine must keep streaming: MusicBee.exe is x86 and not
  large-address-aware.
- The plugin loads Core's net48 dependencies (System.Text.Json and what it pulls in) from a
  private folder through `AssemblyResolve`, because MusicBee has no binding redirects. The
  store format does not change.
- The shell's reusable controls move into a multi-targeted WinForms library. `MainForm` stays
  standalone-only.
- While faderio plays in MusicBee, MusicBee's now-playing, play counts, scrobbling and media
  keys do not see it.
