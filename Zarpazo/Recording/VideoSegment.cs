namespace Zarpazo.Recording;

/// <summary>One piece of the recording that goes into the edited video: where it starts and ends in the original, in seconds.</summary>
/// <param name="Muted">The piece plays without its sound.</param>
/// <param name="Speed">How fast the piece plays: 2 takes half the time, 0.5 twice as long. Anything but 1 plays without sound.</param>
internal readonly record struct VideoSegment(double Start, double End, bool Muted = false, double Speed = 1)
{
    /// <summary>The speeds offered in the editor.</summary>
    public static readonly double[] Speeds = [0.5, 0.75, 1, 1.5, 2, 3, 4];

    /// <summary>How long the piece is in the original.</summary>
    public double Length => End - Start;

    /// <summary>How fast it plays; 1 for a value that makes no sense (such as the 0 of an empty piece).</summary>
    public double Rate => Speed > 0 ? Speed : 1;

    /// <summary>How long the piece lasts in the edited video, with its speed.</summary>
    public double PlayLength => Length / Rate;

    /// <summary>Plays faster or slower than it was recorded.</summary>
    public bool IsRetimed => Rate != 1;

    /// <summary>No sound comes out of it: muted, or played at another speed.</summary>
    public bool IsSilent => Muted || IsRetimed;

    /// <summary>The moment of the original that plays <paramref name="seconds"/> after the piece begins in the edited video.</summary>
    public double ToOriginal(double seconds) => Start + Math.Clamp(seconds, 0, PlayLength) * Rate;

    /// <summary>How long after the piece begins in the edited video a moment of the original plays.</summary>
    public double ToPlay(double original) => Math.Clamp(original - Start, 0, Length) / Rate;
}
