using System.Diagnostics;

namespace Zarpazo.Recording;

/// <summary>
/// The time of a recording: it runs while recording and stands still while paused, so a pause leaves no gap in the picture
/// or the sound. Shared by the capture threads and the window that asks to pause.
/// </summary>
internal sealed class RecordingClock
{
    private readonly Stopwatch _watch = new();
    private readonly object _lock = new();
    private long _pausedTotalMs;
    private long _pausedSinceMs;
    private bool _paused;

    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _paused;
            }
        }
    }

    public long ElapsedMilliseconds
    {
        get
        {
            lock (_lock)
            {
                return (_paused ? _pausedSinceMs : _watch.ElapsedMilliseconds) - _pausedTotalMs;
            }
        }
    }

    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(ElapsedMilliseconds);

    public void Start() => _watch.Start();

    /// <summary>Stops for good; the time read afterwards stays where it was.</summary>
    public void Stop() => _watch.Stop();

    public void Pause()
    {
        lock (_lock)
        {
            if (!_paused)
            {
                _pausedSinceMs = _watch.ElapsedMilliseconds;
                _paused = true;
            }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_paused)
            {
                _pausedTotalMs += _watch.ElapsedMilliseconds - _pausedSinceMs;
                _paused = false;
            }
        }
    }
}
