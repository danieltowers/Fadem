# Audit the engine against v2's intent

Type: task
Status: resolved
Blocked by:

## Question

Nothing to decide: find out how much of Phase 1 the engine already holds. For each in-scope
feature on the map, record three facts against v2's issues 0003, 0008, 0012, 0013 and 0014 and
v2's spec:

- **Present?** Which `PlaybackEngine` commands and `View` fields already serve it
  (`Remove`, `Clear`, `Next`, `Previous`, `PlayRow`, `SetShuffle`, `SetRepeat`, `Unauthor` are
  known to exist).
- **Tested?** Which tests reach it, and at which of ADR 0002's six seams.
- **Reached by the shell?** Whether any control, menu item or launch argument calls it.

Then list where v2's intent is missing or differs: for example folder add, reorder, M3U, skip
behaviour while a transition is in flight, repeat-one's self-adjacency, the authored
end-of-queue fade-out, a broken file mid-queue, the WaveOut fallback.

Agent alone. The answer is a table in this ticket, not code. Tickets 02 and 05 wait on it.

## Answer

Audited 2026-09-23 against v3 `main` at `7c81499`, reading v2 issues 0003, 0008, 0012, 0013
and 0014 and v2's spec (stories 1, 4–6, 19–25, 27, 31, 34, 36). Findings come from reading the
code. Nothing was run, and no build issue should cite them as measured.

**Seam labels.** These are ADR 0002's six seams. **fake** means a whole engine on
`FakeAudioOutput` with real WAVs through the production probe and decoder (`TestEngine`).
**real** means the real-input matrix (FLAC/MP3 fixtures). **unit** means a pure class tested
directly, with no engine. **presenter** and **form** mean `Presenter` and the headless
`MainForm`. **human** means `docs/smoke.md`.

### The table

| Feature (v2 source) | Present in the engine? | Tested? (seam: test) | Reached by the shell? |
|---|---|---|---|
| Add files (0003, story 1) | `AddTracks(paths)` → `AddReport` | fake + form: `ARejectedFile…`, `AddTwoFiles…`; real: matrix | **Yes**: "Add files…" button, launch args |
| Add folder (0003, story 1) | **No.** `AudioProbe` rejects a directory path as a missing file. Nothing enumerates. | none | No |
| Remove a row | `Remove(RowId)`. Removing the sounding row restarts at the same index. | **none** | No |
| Clear the queue | `Clear()` | **none** | No |
| Reorder rows | **No.** No engine member moves a row. Not in v2's intent either; it comes from this map's Notes. | none | No |
| Play a row | `PlayRow(RowId)` | fake: `PlayRow_StartsThatRow…`; form: `PlayRow_ThenAdvance…` | **Yes**: double-click |
| Next / Previous (0003, story 4) | `Next()`, `Previous()`. Previous restarts the track if past 3 s, and at the queue head. It ignores repeat-all's wrap. | fake: `NextAndPrevious_MoveThroughTheQueue` | **Yes**: ⏮ ⏭ buttons |
| Hard-cut advance (0003) | Yes, sample-accurate | fake: `Queue_HardCutBoundary…` | yes, through Play |
| M3U save/load (0003, story 31) | **Not in the engine.** `M3UPlaylist` is a static Core class: `Save(path, Track[])` and `Load(path)` → paths. No engine command. | unit: `M3U_SaveLoad…`, `M3U_HasTheExtm3uHeader` | No |
| Shuffle (0003, story 5) | `SetShuffle(bool)`. Shuffles the upcoming rows. Turning it off restores the original order. New rows land among the upcoming ones. | unit: `ShuffleUpcoming…`, `RowIds_SurviveAShuffle`; fake: `Shuffle_FiresAuthoredTransitions…`, `ShuffleOnThenOff…` | No |
| Repeat off/all/one (0003, story 6) | `SetRepeat(RepeatMode)`. `View.Repeat` and `View.Shuffle` are published. | unit: `RepeatAll_Wraps…`, `RepeatOne…`; fake: see the next two rows | No |
| Repeat-all wrap is a real adjacency (0008, story 20) | Yes, through `IndexAfter` | fake: `RepeatAll_TreatsTheWrapAsARealAdjacency` | No |
| Repeat-one self-adjacency A→A (0008, story 21) | Yes. Without an authored transition it restarts plainly. | fake: `RepeatOne_FiresAnAuthoredSelfAdjacency`, `…PlainlyRestarts` | No |
| Skip mid-transition abandons it (0008, story 24) | `Next()` while overlapping removes the outgoing voice and snaps the incoming voice to full. The **incoming track keeps playing**, so "skip" lands on the track that was fading in. | fake: `SkipMidTransition_AbandonsIt…` | via ⏭ |
| Skip lets the fade finish (0008, story 25) | `SkipBehaviour.LetOutgoingFadeFinish`. The outgoing fade completes and the listener stays on the incoming track. Fixed at construction. | fake: `SkipWithLetFinishSetting…` | No. The setting is unreachable. |
| Seek mid-transition cancels it (0008, story 23) | `Seek`. Seeking the outgoing track removes the incoming one. A seek past the fade-out point does not force the fade. | fake: `SeekDuringACrossfade…`, `SeekPastTheAuthoredFadeOutPoint…` | via the seek bar |
| End-of-queue fade-out (story 19) | **Present, but it borrows.** With no next track, `ScheduleBoundary` uses `_store.FindForOutgoing(path)`: the *first* stored transition whose outgoing track is this one, whatever its incoming track. | fake: `EndOfQueue_AuthoredFadeOutPlaysOut…` (authors A→B, queues only A) | Implicitly: it fires whenever an A→x exists |
| Unauthored adjacency: hard cut or global crossfade (stories 17–18) | Resolver, `DefaultAdjacencyBehaviour`. Fixed at construction. | unit: resolver; fake: `PlaybackEngineSettingsTests` | No. The setting is unreachable. |
| Broken file at play time (0014, story 36) | `StartTrack` catches an open failure and an empty source, raises a notice, and skips to the next track. Everything broken → finish. | fake: `FileThatFailsAfterPassingTheProbe…`, `AllFilesBroken…` | Notice → status strip |
| Broken file at add time | Rejected with its reason (ADR 0007) | fake: `MissingFile…`, `CorruptFile…`; presenter: `Summarise…`; form | **Yes** |
| Audio shorter than `Length` | One notice (`NoticeShortfall`), and the track ends where the audio ends | fake: `AudioEndingBeforeItsLength_IsOneNotice_AndTheBoundaryFiresWhereTheAudioEnds` | Notice |
| WaveOut fallback (0014) | In the **shell's** `NAudioAudioOutput.Initialise`: if `Init` fails, it falls back to WaveOut with a notice. It keeps the queried rate. | **none**: no test references `NAudioAudioOutput` | Startup notice. The title still names the requested mode. |
| Device stops mid-play | `OnOutputStopped(error)` raises a notice and publishes. Voices and `_finished` are left as they were. | none | Notice |
| Settings persistence (0013) | `PlayerSettings` record (output mode, default adjacency, crossfade N, skip, ReplayGain, preview pre-roll, visualiser). `SettingsStore` loads and saves atomically, with defaults, a corrupt-file warning and a newer-schema warning. | unit: `SettingsStoreTests` (5) | **No.** `Program` never loads `SettingsStore` and builds the engine with defaults. Output mode comes only from `--output=`. |
| ReplayGain (0012, story 34) | Tags read at add time (ID3v2 TXXX, Xiph, APE), clamped at ±12 dB, applied as base gain before fades. `ReplayGainEnabled` defaults to **true**. It is fixed at construction and applied when a voice is created. | fake: `ReplayGain_TrackGainApplies…`, `ReplayGainDisabled…`; unit: clamp, dB | No, beyond the default being on |
| Author / Capture | `Author`, `Capture`, `Transition` | fake/seam: `SeamTests`; form: `Capture_…`, `Save_…` | **Yes**: the editor |
| Unauthor / reset (story 27) | `Unauthor(Adjacency)` reschedules a pending boundary | fake: `Unauthor_CancelsAPendingBoundary` | **No.** Saving with every field blank calls `Author` with an empty spec. The store keeps it and the resolver treats it as unauthored. |
| Curve selection (story 12) | `TransitionSpec.Curve`, which the resolver and mixer honour | unit: resolver, `CrossfadeShapeTests`; store round-trip | **No.** The editor has no curve control. |
| From-end points (story 9) | Fade-out: `FadePointSpec.FromEnd`, which the resolver honours. **Fade-in: the resolver ignores `FromEnd`** and uses the position as from-start. | unit: resolver; `FadeEngineAccuracyTests` (fade-out from end) | **No.** The editor always writes `FromEnd: false`. |
| Preview (0010), visualiser/peak (0011) | **Absent.** Only the `PreviewPreRollSeconds` and `VisualiserVisible` settings exist. `Renderer.RenderBoundary` renders one boundary offline to WAV. | `RendererTests` for the render | The render button, not preview |
| Queue persistence | Absent (absent in v2 too) | — | — |

### Where v2's intent is missing or differs

Queue management (ticket 12, created from this audit):

1. **Folder add is absent** in the engine and the shell. Only file paths are probed. Ticket 07
   asks whether a folder needs a background probe; nothing yet says what "add folder" enumerates.
2. **Reorder is absent** in the engine and in v2's intent. It is new with this map.
3. **M3U is a bare file format, not a feature.** There is no engine call to save or load the
   queue, so a shell doing it would read `View.Queue`, write the file, then call `AddTracks`. That
   fails placement test 1. `Load` returns relative lines unresolved, so they resolve against the
   process working directory, not the playlist's folder. `#EXTINF` writes `Length` rounded to
   whole seconds. Load does not say whether it replaces or appends to the queue.
4. **Remove, Clear, Shuffle and Repeat exist in the engine and have no shell.** `Remove` and
   `Clear` have **no tests at any seam**.

Skip, repeat and end of queue (ticket 02):

5. **End-of-queue fade-out borrows another adjacency's transition.** `FindForOutgoing` returns
   the first stored A→x transition, in dictionary order, for an unrelated boundary. An author
   who wrote A→B gets that fade at the queue's end even though B never comes. With several A→x
   entries, which one fires is arbitrary. v2's story 19 says "the authored fade-out"; nothing
   authors an end-of-queue fade as such.
6. **Skip mid-transition lands on the incoming track.** Both skip behaviours resolve the
   overlap and stay on the track that was fading in. "Starts the next adjacency with its own
   transition" happens only because that track's boundary is already scheduled. The user may
   expect a skip to go *past* the incoming track.
7. **A manual Next outside a transition always hard-cuts from 0.** It never uses an authored
   fade-in point for the next adjacency. A manual Next also ignores an authored fade-out.
8. **Previous does not wrap** under repeat-all, and it uses a fixed 3 s restart threshold.

Broken files and the device (ticket 05):

9. **A decode error after the file opens is not skipped.** The mixer has no catch.
   `EngineFrameSource.Read` catches, renders the block as silence and raises a notice, then
   fails again on the next block. The likely result is silence, a notice on every block, and a
   playhead that keeps moving. Inferred by reading; untested.
10. **A broken incoming track at an authored boundary.** `SkipToNextAfter` starts the next
    track at the fade trigger with no spawn, while `_outgoingVoice` is still set and fading.
    Untested.
11. **Device loss leaves the engine half-stopped.** `OnOutputStopped` sets no `_finished` and
    keeps the voices, so what `Play()` does afterwards is untested.
12. **WaveOut fallback is untested**, and the window title reports the requested mode, not the
    one in use.
13. **A store write failure escapes `Author` and `Unauthor`.** `Persist` exceptions are not
    caught under `Run`, which contradicts "every command is total". `Presenter.OpenTransitionStore`
    has one empty `catch (IOException)` when moving a corrupt file aside, where v2 said "no empty
    catches".

Settings, ReplayGain and the editor (tickets 10, 06, 09):

14. **Settings are fixed at engine construction.** `PlayerSettings` is immutable and read once.
    Changing skip behaviour, default adjacency, the crossfade length or ReplayGain means either
    a restart or a new engine command. ReplayGain is applied when a voice is created, so even a
    live toggle would not affect the track already playing.
15. **Editor completions are shell work plus one resolver gap.** Curve and fade-out-from-end
    need only editor controls, because the engine honours both. Delete/reset needs a control
    over the existing `Unauthor`. Fade-in-from-end is stored but ignored by the resolver: decide
    whether it means anything, or remove it from `TransitionSpec`.

Not-yet-specified audio paths:

16. **No automated test reaches the production output** (`NAudioAudioOutput`). Its device
    clock, fallback and stop handling are proven only by the human smoke checklist.

**Scale.** Queue semantics, skip, repeat and shuffle, settings storage, ReplayGain and
add-time rejection are engine-complete and tested at the fake seam. What remains:

- **Mostly shell:** transport toggles, queue editing, the settings dialog, the editor controls.
- **New engine members:** folder add, reorder, M3U as queue commands, and settings that change
  at runtime.
- **Fixes to existing engine behaviour:** the end-of-queue lookup, play-time decode failures,
  device loss.
- **Built from scratch:** preview and the visualiser.
