using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace Zarpazo.Recording;

/// <summary>
/// Everything about an edit that can be worked on again: the pieces kept (with their speed), the zooms, the blurs, texts and
/// arrows, the crop and the look. It is what the editor writes next to a recording in the history, so "Editar" there opens the
/// recording as it was left instead of the finished video. Plain data with a format of its own (not the editor's types), so
/// the file stays readable when those change; one that cannot be read is left out and the recording opens as a new one.
/// </summary>
internal sealed class VideoProject
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int Version { get; set; } = CurrentVersion;

    /// <summary>The length of the recording this edit was made on, in seconds.</summary>
    public double SourceDuration { get; set; }

    public List<SegmentData> Segments { get; set; } = [];

    public List<ZoomData> Zooms { get; set; } = [];

    public List<OverlayData> Overlays { get; set; } = [];

    public CropData Crop { get; set; } = new(0, 0, 0, 0);

    public VideoLook? Look { get; set; }

    public sealed record SegmentData(double Start, double End, bool Muted, double Speed);

    public sealed record ZoomData(double Start, double End, double Scale, double FocusX, double FocusY);

    /// <param name="Kind">The <see cref="OverlayKind"/> as a number.</param>
    /// <param name="Ink">The colour as #AARRGGBB; null for the usual one of its kind.</param>
    public sealed record OverlayData(int Kind, double Start, double End, double X, double Y, double X2, double Y2, string Text, string? Ink, double Size);

    public sealed record CropData(double Left, double Top, double Right, double Bottom);

    /// <summary>The edit as it is now.</summary>
    public static VideoProject Capture(EditTimeline timeline, VideoLook look) => new()
    {
        SourceDuration = timeline.SourceDuration,
        Segments = [.. timeline.Segments.Select(segment => new SegmentData(segment.Start, segment.End, segment.Muted, segment.Speed))],
        Zooms = [.. timeline.Zooms.Select(zoom => new ZoomData(zoom.Start, zoom.End, zoom.Scale, zoom.FocusX, zoom.FocusY))],
        Overlays = [.. timeline.Overlays.Select(overlay => new OverlayData((int)overlay.Kind, overlay.Start, overlay.End, overlay.X, overlay.Y,
            overlay.X2, overlay.Y2, overlay.Text, overlay.Ink.A == 0 ? null : overlay.Ink.ToString(), overlay.Size))],
        Crop = new CropData(timeline.Crop.Left, timeline.Crop.Top, timeline.Crop.Right, timeline.Crop.Bottom),
        Look = look,
    };

    public IReadOnlyList<VideoSegment> ToSegments() =>
        [.. Segments.Select(segment => new VideoSegment(segment.Start, segment.End, segment.Muted, segment.Speed))];

    public IReadOnlyList<ZoomRegion> ToZooms() =>
        [.. Zooms.Select(zoom => new ZoomRegion(zoom.Start, zoom.End, zoom.Scale, zoom.FocusX, zoom.FocusY))];

    public IReadOnlyList<VideoOverlay> ToOverlays() => [.. Overlays.Select(overlay => new VideoOverlay(
        Enum.IsDefined((OverlayKind)overlay.Kind) ? (OverlayKind)overlay.Kind : OverlayKind.Text, overlay.Start, overlay.End, overlay.X, overlay.Y,
        overlay.X2, overlay.Y2, overlay.Text ?? "", ParseInk(overlay.Ink), overlay.Size))];

    public VideoCrop ToCrop() => new VideoCrop(Crop.Left, Crop.Top, Crop.Right, Crop.Bottom).Tidied();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>The project in <paramref name="json"/>; null when it is damaged or comes from a newer Zarpazo.</summary>
    public static VideoProject? FromJson(string json)
    {
        try
        {
            VideoProject? project = JsonSerializer.Deserialize<VideoProject>(json, JsonOptions);
            return project is { Version: >= 1 and <= CurrentVersion, Segments: not null, Zooms: not null, Overlays: not null, Crop: not null } ? project : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public void Save(string path) => File.WriteAllText(path, ToJson());

    /// <summary>The project in the file; null when there is none or it cannot be read.</summary>
    public static VideoProject? Load(string path)
    {
        try
        {
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Color ParseInk(string? ink)
    {
        try
        {
            return string.IsNullOrEmpty(ink) ? default : (Color)ColorConverter.ConvertFromString(ink);
        }
        catch (FormatException)
        {
            return default;
        }
    }
}
