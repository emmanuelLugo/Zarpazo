namespace Zarpazo.Recording;

/// <summary>Mixes the sound sources into chunks on a steady schedule and hands them to the video file, each stamped with its place in time.</summary>
internal sealed class AudioRecorder : IDisposable
{
    private readonly IReadOnlyList<AudioInput> _inputs;
    private readonly Mp4Writer _writer;
    private readonly RecordingClock _clock;
    private readonly AudioMixer _mixer;
    private readonly int _offsetMs;
    private Thread? _thread;
    private volatile bool _stopRequested;

    /// <param name="clock">The recording's clock, shared with the video so that picture and sound start together.</param>
    /// <param name="offsetMs">Moves the sound against the picture: negative plays it earlier, positive later.</param>
    public AudioRecorder(IReadOnlyList<AudioInput> inputs, Mp4Writer writer, RecordingClock clock, int offsetMs = 0)
    {
        _offsetMs = offsetMs;
        _inputs = inputs;
        _writer = writer;
        _clock = clock;
        _mixer = new AudioMixer(inputs.Select(input => input.Samples).ToList());
    }

    /// <summary>How loud each source is at the moment.</summary>
    public IReadOnlyList<AudioMeter> Meters =>
        _inputs.Select((input, i) => new AudioMeter(input.Name, input.IsMicrophone, _mixer.Levels[i], _mixer.Gains[i])).ToList();

    /// <summary>Changes the volume of one source (0 mutes it) while recording.</summary>
    public void SetGain(int source, float gain) => _mixer.SetGain(source, gain);

    public void Start()
    {
        foreach (AudioInput input in _inputs)
        {
            input.Start();
        }
        _thread = new Thread(Run) { IsBackground = true, Name = "Zarpazo audio" };
        _thread.Start();
    }

    /// <summary>Drops what the devices have gathered, so that after a pause the sound is from now and not from before it.</summary>
    public void DiscardBuffered()
    {
        foreach (AudioInput input in _inputs)
        {
            input.Clear();
        }
    }

    public void Stop()
    {
        _stopRequested = true;
        _thread?.Join();
    }

    private void Run()
    {
        long chunk = 0;
        if (_offsetMs > 0)
        {
            _writer.AddAudio(AudioTiming.Silence(_offsetMs), TimeSpan.Zero); // the sound starts late, so what comes before it is silent
        }
        while (!_stopRequested)
        {
            if (_clock.IsPaused)
            {
                Thread.Sleep(10); // the clock stands still, so no chunk falls due
                continue;
            }

            // A chunk is made once the time it covers has passed, so there is that much sound to take.
            long due = (chunk + 1) * AudioMixer.ChunkMilliseconds;
            long now = _clock.ElapsedMilliseconds;
            if (now < due)
            {
                Thread.Sleep((int)Math.Max(1, due - now));
                continue;
            }

            foreach (AudioInput input in _inputs)
            {
                input.TrimBacklog();
            }
            // The time stamp comes from counting chunks, not from the clock: the sound stays one unbroken stream.
            if (AudioTiming.Shift(_mixer.NextChunk(), chunk * AudioMixer.ChunkMilliseconds, _offsetMs) is var (pcm, time))
            {
                _writer.AddAudio(pcm, TimeSpan.FromMilliseconds(time));
            }
            chunk++;
        }
    }

    public void Dispose()
    {
        Stop();
        foreach (AudioInput input in _inputs)
        {
            input.Dispose();
        }
    }
}
