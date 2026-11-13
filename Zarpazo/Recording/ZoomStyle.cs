namespace Zarpazo.Recording;

/// <summary>
/// How a video's zooms look, apart from where they are: how close they go and how the camera moves. A style, like the frame
/// and the pointer, so it is remembered for the next video and kept in presets; the zooms themselves belong to one video.
/// </summary>
/// <param name="Depth">How close every zoom goes (<see cref="ZoomRegion.MinScale"/> to <see cref="ZoomRegion.MaxScale"/>).</param>
internal sealed record ZoomStyle(double Depth, ZoomMotion Motion)
{
    public static ZoomStyle Default { get; } = new(ZoomRegion.DefaultScale, ZoomMotion.Smooth);

    /// <summary>The same style with its values brought inside what is offered (a hand-edited settings file).</summary>
    public ZoomStyle Tidied() => this with
    {
        Depth = Math.Round(Math.Clamp(double.IsFinite(Depth) ? Depth : Default.Depth, ZoomRegion.MinScale, ZoomRegion.MaxScale), 2),
        Motion = Enum.IsDefined(Motion) ? Motion : Default.Motion,
    };
}
