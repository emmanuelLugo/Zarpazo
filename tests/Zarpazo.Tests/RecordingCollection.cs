namespace Zarpazo.Tests;

/// <summary>
/// The tests that record the screen, encode video or depend on the clock. Run side by side they fight over the processor and the
/// encoder, and what they measure (frames kept, milliseconds) then drifts, so they take turns instead.
/// </summary>
[CollectionDefinition("Recording", DisableParallelization = true)]
public sealed class RecordingCollection
{
}
