namespace Fadem.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Device loss and reopening (build issue 11) ───────────────────────────────────────

    /// <summary>The device stopped on its own. A clean end needs nothing; a failure (headphones
    /// unplugged) lands paused where the listener was, with a notice. It never resumes by itself:
    /// sound would go to the speakers the headphones were unplugged from.</summary>
    private void OnOutputStopped(Exception? error)
    {
        if (error is null)
        {
            return;
        }
        Run(() =>
        {
            if (_disposed)
            {
                return;
            }
            _outputLost = true;
            if (_preview is not null)
            {
                EndPreview(); // the held queue stays paused where it was held
                RaiseNotice(TransportNow == Transport.Paused
                    ? $"Audio output lost ({error.Message}). The preview ended; the queue stays paused where it was held."
                    : $"Audio output lost ({error.Message}). The preview ended.");
                return;
            }
            ParkWhereHeard();
            RaiseNotice(TransportNow == Transport.Paused
                ? $"Audio output lost ({error.Message}). Playback is paused; press Play to resume."
                : $"Audio output lost ({error.Message}). Press Play to try again.");
            Publish();
        });
    }

    /// <summary>Releases every voice and lands where the listener was, as one fresh voice at full
    /// volume on a timeline back at zero: the track the view shows attended, at its heard
    /// playhead, so a hard cut or a last track's tail still in the device buffer is not lost.
    /// Mid-transition it is the incoming track, as a skip would land. A track that cannot be
    /// opened again is passed over to the next readable row, from its start.</summary>
    private void ParkWhereHeard()
    {
        ObserveClock(); // a handover or an end the listener already heard
        var view = View;
        var heard = view.Incoming ?? view.Attended;
        var index = heard is null ? -1 : _queue.IndexOf(heard.Row.Id);
        ResetForRestart();
        StopOutputOrNotice();
        if (index < 0 || OpenFirstReadable(index, RowAfter, retryMarked: true, intoPassing: true) is not { } landing)
        {
            return; // nothing was playing, or nothing is readable: stopped
        }
        StartTrack(landing, 0, null, from: landing.Index == index ? heard!.Playhead.Frames(_rate) : 0);
        _parked = true;
    }

    /// <summary>Stops the device, so its emitted count returns to the timeline's zero. Stopping a
    /// device that has gone can fail too; that is a notice, never an exception out of the
    /// device's event or a render.</summary>
    private void StopOutputOrNotice()
    {
        try
        {
            StopOutput();
        }
        catch (Exception e)
        {
            RaiseNotice($"Audio output error: {e.Message}");
        }
    }

    /// <summary>Starts the device, reopening a lost one on the current default device first. With
    /// no device, playback stays paused with a notice. A device at another rate is converted to,
    /// and the engine keeps its rate (ADR 0008): the notice suggests a restart.</summary>
    private void PlayOutput()
    {
        if (StartDevice())
        {
            _parked = false;
        }
    }

    /// <summary>Starts the device for the queue or a preview, reopening a lost one first. False,
    /// with a notice, when there is no device.</summary>
    private bool StartDevice()
    {
        if (_outputLost)
        {
            var deviceRate = 0;
            try
            {
                HaltOutput(() => deviceRate = _output.Reopen()); // it closes the failed player
            }
            catch (Exception e)
            {
                RaiseNotice($"No audio output device to play on ({e.Message}). Playback stays paused.");
                return false;
            }
            _outputLost = false;
            if (deviceRate != _rate)
            {
                RaiseNotice(
                    $"The output device runs at {deviceRate} Hz; Fadem mixes at {_rate} Hz and converts. " +
                    "Restart Fadem to mix at the device's rate.");
            }
        }
        _output.Play();
        return true;
    }
}
