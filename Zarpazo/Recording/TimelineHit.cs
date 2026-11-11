namespace Zarpazo.Recording;

/// <summary>What is under the pointer on the timeline, and which piece it belongs to.</summary>
/// <param name="Index">The piece it is over; -1 on the ruler or empty space.</param>
internal readonly record struct TimelineHit(TimelinePart Part, int Index);
