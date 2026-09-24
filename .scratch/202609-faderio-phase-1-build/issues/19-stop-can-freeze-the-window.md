# 19: Stop can freeze the window

Status: needs-triage
Blocked by: —

Found during the Looks smoke run for [issue 03](03-open-and-save-playlists.md) on 2026-09-23. Not
caused by it: neither `Stop` nor the output changed there.

## What happened

WASAPI shared, playing the 440 Hz test tone at 0:03. A UI Automation invoke of ⏹ timed out, and
the window stopped updating: the readout stayed at `0:03 / -0:17`, the play button kept its
pause glyph, and no timer render happened for minutes. `WM_NULL` was still answered in 0 ms and
the process reported Responding, but every UI Automation query timed out. The process had to
be killed. Seen once; not reproduced.

## Suspected cause (from reading the code, not from a stack)

`PlaybackEngine.Stop` calls `_output.Stop()` inside `Run`, so under the engine lock.
`NAudioAudioOutput.Stop` then calls `IWavePlayer.Stop()`, and `WasapiOut.Stop` joins its
playback thread. If that thread has already passed `CountingSource.Read`'s stopped check and is
waiting on the engine lock inside `EngineFrameSource.Read`, neither thread can proceed. The
join on the STA UI thread still pumps COM messages, which fits `WM_NULL` being answered while the
timer and UI Automation are not.

`CountingSource.InRead` protects only the case where `Stop` is called from the render thread.

## To decide

- Whether the player is stopped outside the engine lock (the engine records the stop under it,
  the output is told after `Run` releases it), or `Read` stops taking the lock once a stop is
  requested.
- A test: the fake output cannot show this, since it has no second thread joining. A test
  against `NAudioAudioOutput` is out of scope for phase 1 (spec), so this may stay a smoke item:
  press ⏹ repeatedly while playing and confirm the window keeps rendering.

## Comments

- 2026-09-24, during the Looks smoke run for [issue 04](04-remove-rows-and-clear-the-queue.md).
  The suspected cause is confirmed by `dotnet-stack` on a frozen window. **Clear queue → OK**
  while playing froze it:
  - The UI thread was in `MainForm.ClearQueue` → `PlaybackEngine.Clear` → `Run` (holding the
    lock) → `NAudioAudioOutput.Stop` → `WasapiOut.Stop` → `Thread.Join`.
  - The WASAPI `PlayThread` was in `WasapiOut.FillBuffer` → `CountingSource.Read` →
    `EngineFrameSource.Read` → `PlaybackEngine.Run`, waiting on the same lock.
  - As before, `WM_NULL` was answered in 0 ms and UI Automation timed out.
- There are now more ways in. Every `_output.Stop()` runs under the lock: `Stop`, `Clear`, and
  `FinishPlayback`, which is reached by removing the last playing row or the only one, and at
  the natural end of the queue.
- Trials on WASAPI shared, one 12 s tone playing for 2 s, then the action, then 1.5 s to check
  the stacks:
  - **Delete on the only playing row:** froze 1 in 4.
  - **⏹:** froze 0 in 4.
  - **Clear → OK while playing:** froze 1 in 2.
  The race is real and not rare.

