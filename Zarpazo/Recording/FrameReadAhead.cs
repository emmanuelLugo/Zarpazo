using System.Collections.Concurrent;

namespace Zarpazo.Recording;

/// <summary>
/// Reads a video's pictures on a thread of its own, a few ahead of the one being drawn, so decoding the next picture goes on
/// while the current one is composed. Each picture lives in a buffer that is lent out until it is given back.
/// </summary>
internal sealed class FrameReadAhead : IDisposable
{
    private const int Buffers = 4; // the one drawn, the next, one being decoded and one waiting

    private readonly BlockingCollection<byte[]> _free = [];
    private readonly BlockingCollection<(byte[] Pixels, TimeSpan Time)> _ready = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private Exception? _failure;

    /// <param name="path">The video file.</param>
    /// <param name="width">The size of the pictures wanted (see <see cref="VideoFrameReader"/>).</param>
    public FrameReadAhead(string path, int width, int height)
    {
        for (int i = 0; i < Buffers; i++)
        {
            _free.Add(new byte[width * height * 4]);
        }
        _thread = new Thread(() => Read(path, width, height)) { IsBackground = true, Name = "Zarpazo frame read-ahead" };
        _thread.Start();
    }

    private void Read(string path, int width, int height)
    {
        try
        {
            using var reader = new VideoFrameReader(path, width, height);
            while (true)
            {
                byte[] pixels = _free.Take(_stop.Token);
                if (!reader.TryRead(pixels, out TimeSpan time))
                {
                    break;
                }
                _ready.Add((pixels, time), _stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _failure = ex;
        }
        finally
        {
            _ready.CompleteAdding();
        }
    }

    /// <summary>The next picture, waiting for it if it is not decoded yet. Give the buffer back with <see cref="Give"/> when done with it.</summary>
    /// <returns>False at the end of the video.</returns>
    /// <exception cref="Exception">Whatever stopped the reading, as it was.</exception>
    public bool TryTake(out byte[] pixels, out TimeSpan time)
    {
        if (_ready.TryTake(out (byte[] Pixels, TimeSpan Time) item, Timeout.Infinite, _stop.Token))
        {
            (pixels, time) = item;
            return true;
        }
        if (_failure is { } failure)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        pixels = [];
        time = default;
        return false;
    }

    /// <summary>Hands a buffer taken with <see cref="TryTake"/> back so the next picture can be decoded into it.</summary>
    public void Give(byte[] pixels)
    {
        if (!_free.IsAddingCompleted)
        {
            _free.Add(pixels);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join();
        _stop.Dispose();
    }
}
