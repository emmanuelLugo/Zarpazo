namespace Zarpazo.Recording;

internal enum RecordingFormat
{
    Gif,
    Video,
}

/// <summary>What to record: the kind of file and, for a video, which sound goes into it.</summary>
/// <param name="Microphone">Put the microphone in the video.</param>
/// <param name="SystemSound">Put what Windows is playing (the speakers or headphones) in the video.</param>
/// <param name="MicrophoneId">Which microphone; empty for the one Windows uses by default.</param>
/// <param name="SystemSoundId">Which speakers or headphones to take the sound from; empty for the default ones.</param>
/// <param name="HighlightCursor">Draw a halo around the pointer and a ring at every click.</param>
/// <param name="ShowKeys">Show the shortcuts pressed (never the letters typed).</param>
/// <param name="CursorScale">How big the pointer is drawn: 1 as on screen, up to 2.</param>
internal sealed record RecordingOptions(
    RecordingFormat Format,
    bool Microphone,
    bool SystemSound,
    string MicrophoneId = "",
    string SystemSoundId = "",
    bool HighlightCursor = false,
    bool ShowKeys = false,
    double CursorScale = 1)
{
    /// <summary>A GIF has no sound, so the choices only count for a video.</summary>
    public bool HasSound => Format == RecordingFormat.Video && (Microphone || SystemSound);

    public static RecordingOptions Default { get; } = new(RecordingFormat.Gif, Microphone: false, SystemSound: false);
}
