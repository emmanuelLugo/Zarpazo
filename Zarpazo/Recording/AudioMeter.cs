namespace Zarpazo.Recording;

/// <summary>How loud one sound source is at the moment, for the meter in the recording bar.</summary>
/// <param name="Level">0 for silence to 1 for full scale.</param>
internal sealed record AudioMeter(string Name, bool IsMicrophone, float Level);
