namespace Zarpazo.Recording;

/// <summary>Mixes the sound sources into chunks on a steady schedule and hands them to the video file, each stamped with its place in time.</summary>
internal sealed class AudioRecorder : IDisposable
{
    private readonly IReadOnlyList<AudioInput> _inputs;
    private readonly Mp4Writer _writer;
    private readonly RecordingClock _clock;
    private readonly AudioMixer _mixer;
    private Thread? _thread;
    private volatile bool _stopRequested;

    /// <param name="clock">The recording's clock, shared with the video so that picture and sound start together.</param>
    public AudioRecorder(IReadOnlyList<AudioInput> inputs, Mp4Writer writer, RecordingClock clock)
    {
        _inputs = inputs;
        _writer = writer;
        _clock = clock;
        _mixer = new AudioMixer(inputs.Select(input => input.Samples).ToList());
    }

    /// <summary>How loud each source is at the moment.</summary>
    public IReadOnlyList<AudioMeter> Meters =>
        _inputs.Select((input, i) => new AudioMeter(input.Name, input.IsMicrophone, _mixer.Levels[i])).ToList();

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
            _writer.AddAudio(_mixer.NextChunk(), TimeSpan.FromMilliseconds(chunk * AudioMixer.ChunkMilliseconds));
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
