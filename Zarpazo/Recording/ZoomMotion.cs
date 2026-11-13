namespace Zarpazo.Recording;

/// <summary>How the camera moves into and out of the zooms (see <see cref="ZoomTiming"/>).</summary>
internal enum ZoomMotion
{
    /// <summary>Slow and gentle, for presentations: the times Zarpazo always had.</summary>
    Smooth,

    /// <summary>Quick, for tutorials that go from click to click.</summary>
    Focused,
}
