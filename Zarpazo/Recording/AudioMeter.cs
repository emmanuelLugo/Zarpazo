namespace Zarpazo.Recording;

/// <summary>How loud one sound source is at the moment, and how much of it goes into the recording.</summary>
/// <param name="Level">0 for silence to 1 for full scale, as recorded (after the volume).</param>
/// <param name="Gain">The volume of the source: 0 is muted, 1 is as it comes, 2 is twice as loud.</param>
internal sealed record AudioMeter(string Name, bool IsMicrophone, float Level, float Gain = 1f);
