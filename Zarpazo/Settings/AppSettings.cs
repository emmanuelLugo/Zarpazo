using System.Text.Json.Serialization;
using Zarpazo.Shell;

namespace Zarpazo.Settings;

/// <summary>What the person can change. Plain values, so the file is easy to read and to edit by hand.</summary>
internal sealed class AppSettings
{
    public const string DefaultColor = "#E53935";
    public const double DefaultThickness = 4;
    public const int DefaultHistoryLimit = 100;
    public const int DefaultJpegQuality = 92;

    /// <summary>The JPG qualities the settings window offers.</summary>
    public static readonly int[] JpegQualityChoices = [60, 75, 85, 92, 100];

    /// <summary>The countdowns the settings window offers.</summary>
    public static readonly int[] CountdownChoices = [0, 3, 5];

    /// <summary>The sound offsets the settings window offers, in milliseconds.</summary>
    public static readonly int[] AudioOffsetChoices = [-200, -100, -50, 0, 50, 100, 200];

    public uint HotKeyModifiers { get; set; } = HotKeyBinding.Default.Modifiers;
    public uint HotKeyVirtualKey { get; set; } = HotKeyBinding.Default.VirtualKey;
    public uint RecordHotKeyModifiers { get; set; } = HotKeyBinding.DefaultRecord.Modifiers;
    public uint RecordHotKeyVirtualKey { get; set; } = HotKeyBinding.DefaultRecord.VirtualKey;
    public bool CopyAsFile { get; set; } = true;

    /// <summary>Where the Save dialog opens. Empty means the Pictures folder.</summary>
    public string SaveFolder { get; set; } = "";

    /// <summary>"png" or "jpg".</summary>
    public string SaveFormat { get; set; } = "png";

    /// <summary>How much a JPG is compressed: lower is smaller and blurrier. Only matters for JPG.</summary>
    public int JpegQuality { get; set; } = DefaultJpegQuality;

    /// <summary>Keep every copied or saved capture in the history.</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>Also save every capture that is copied, and every recording, to <see cref="AutoSaveFolder"/> without asking.</summary>
    public bool AutoSave { get; set; }

    /// <summary>Where the automatic saves go. Empty means Pictures\Zarpazo.</summary>
    public string AutoSaveFolder { get; set; } = "";

    /// <summary>The name of automatic saves, with {fecha}, {hora} and {n}; see <see cref="Output.FileNameTemplate"/>.</summary>
    public string FileNameTemplate { get; set; } = Output.FileNameTemplate.Default;

    /// <summary>The last number {n} took; kept between runs so the numbering carries on.</summary>
    public int AutoSaveCounter { get; set; }

    /// <summary>How many captures the history keeps; the oldest are deleted beyond that.</summary>
    public int HistoryLimit { get; set; } = DefaultHistoryLimit;

    public bool RememberStyle { get; set; } = true;
    public string Color { get; set; } = DefaultColor;
    public double Thickness { get; set; } = DefaultThickness;

    /// <summary>Put every copied or saved capture on a background. The style is kept between captures.</summary>
    public bool BeautifyEnabled { get; set; }
    public string BeautifyBackground { get; set; } = Output.BeautifyStyle.Default.Background;
    public int BeautifyPadding { get; set; } = Output.BeautifyStyle.Default.Padding;
    public int BeautifyRadius { get; set; } = Output.BeautifyStyle.Default.Radius;
    public bool BeautifyShadow { get; set; } = Output.BeautifyStyle.Default.Shadow;

    /// <summary>"gif" or "video"; what the recording panel starts with.</summary>
    public string RecordFormat { get; set; } = "gif";
    public bool RecordMicrophone { get; set; }
    public bool RecordSystemSound { get; set; }
    public bool RecordHighlightCursor { get; set; }
    public bool RecordShowKeys { get; set; }

    /// <summary>Seconds counted down before a recording starts: 0, 3 or 5.</summary>
    public int RecordCountdownSeconds { get; set; } = 3;

    public int GifFramesPerSecond { get; set; } = 12;
    public bool GifDither { get; set; }
    public int GifMaxSeconds { get; set; } = 30;
    public int VideoFramesPerSecond { get; set; } = 30;

    /// <summary>"low", "medium" or "high".</summary>
    public string VideoQuality { get; set; } = "medium";
    public int VideoMaxMinutes { get; set; } = 30;

    [JsonIgnore]
    public Recording.RecordingQuality RecordingQuality
    {
        get => new(
            GifFramesPerSecond,
            GifDither,
            GifMaxSeconds,
            VideoFramesPerSecond,
            VideoQuality switch { "low" => Recording.VideoQuality.Low, "high" => Recording.VideoQuality.High, _ => Recording.VideoQuality.Medium },
            VideoMaxMinutes);
        set
        {
            GifFramesPerSecond = value.GifFramesPerSecond;
            GifDither = value.GifDither;
            GifMaxSeconds = value.GifMaxSeconds;
            VideoFramesPerSecond = value.VideoFramesPerSecond;
            VideoQuality = value.VideoQuality switch { Recording.VideoQuality.Low => "low", Recording.VideoQuality.High => "high", _ => "medium" };
            VideoMaxMinutes = value.VideoMaxMinutes;
        }
    }

    /// <summary>The speakers or headphones "Sonido de Windows" is taken from; empty means the default ones.</summary>
    public string RecordSystemSoundId { get; set; } = "";

    /// <summary>
    /// Milliseconds the sound is moved against the picture in a video: negative plays it earlier, positive later.
    /// For when the sound comes out of step, which depends on the equipment.
    /// </summary>
    public int RecordAudioOffsetMs { get; set; }

    /// <summary>The microphone chosen for videos; empty means the one Windows uses by default.</summary>
    public string RecordMicrophoneId { get; set; } = "";

    [JsonIgnore]
    public Recording.RecordingOptions RecordingOptions
    {
        get => new(RecordFormat == "video" ? Recording.RecordingFormat.Video : Recording.RecordingFormat.Gif, RecordMicrophone, RecordSystemSound, RecordMicrophoneId ?? "", RecordSystemSoundId ?? "",
            RecordHighlightCursor, RecordShowKeys);
        set
        {
            RecordFormat = value.Format == Recording.RecordingFormat.Video ? "video" : "gif";
            RecordMicrophone = value.Microphone;
            RecordSystemSound = value.SystemSound;
            RecordMicrophoneId = value.MicrophoneId;
            RecordSystemSoundId = value.SystemSoundId;
            RecordHighlightCursor = value.HighlightCursor;
            RecordShowKeys = value.ShowKeys;
        }
    }

    [JsonIgnore]
    public Output.BeautifyStyle BeautifyStyle
    {
        get => new(BeautifyBackground, BeautifyPadding, BeautifyRadius, BeautifyShadow);
        set
        {
            BeautifyBackground = value.Background;
            BeautifyPadding = value.Padding;
            BeautifyRadius = value.Radius;
            BeautifyShadow = value.Shadow;
        }
    }

    [JsonIgnore]
    public HotKeyBinding RecordHotKey
    {
        get => new(RecordHotKeyModifiers, RecordHotKeyVirtualKey);
        set
        {
            RecordHotKeyModifiers = value.Modifiers;
            RecordHotKeyVirtualKey = value.VirtualKey;
        }
    }

    [JsonIgnore]
    public HotKeyBinding HotKey
    {
        get => new(HotKeyModifiers, HotKeyVirtualKey);
        set
        {
            HotKeyModifiers = value.Modifiers;
            HotKeyVirtualKey = value.VirtualKey;
        }
    }

    /// <summary>The file can be edited by hand, so values that would break something fall back to the defaults.</summary>
    public void Normalize()
    {
        if (!HotKey.IsAcceptable)
        {
            HotKey = HotKeyBinding.Default;
        }
        if (!RecordHotKey.IsAcceptable || RecordHotKey == HotKey)
        {
            RecordHotKey = HotKeyBinding.DefaultRecord; // unusable, or the same key as the capture shortcut
            if (RecordHotKey == HotKey)
            {
                HotKey = HotKeyBinding.Default; // the capture shortcut was set to the default recording one: it gives way
            }
        }
        SaveFormat = SaveFormat?.ToLowerInvariant() == "jpg" ? "jpg" : "png";
        SaveFolder ??= "";
        if (HistoryLimit < 1)
        {
            HistoryLimit = DefaultHistoryLimit;
        }
        Color ??= DefaultColor;
        AutoSaveFolder ??= "";
        if (string.IsNullOrWhiteSpace(FileNameTemplate))
        {
            FileNameTemplate = Output.FileNameTemplate.Default;
        }
        AutoSaveCounter = Math.Max(0, AutoSaveCounter);
        JpegQuality = Recording.RecordingQuality.Nearest(JpegQuality, JpegQualityChoices);
        VideoQuality = VideoQuality?.ToLowerInvariant() is "low" or "high" ? VideoQuality.ToLowerInvariant() : "medium";
        RecordingQuality = RecordingQuality with
        {
            GifFramesPerSecond = Recording.RecordingQuality.Nearest(GifFramesPerSecond, Recording.RecordingQuality.GifFramesPerSecondChoices),
            GifMaxSeconds = Recording.RecordingQuality.Nearest(GifMaxSeconds, Recording.RecordingQuality.GifMaxSecondsChoices),
            VideoFramesPerSecond = Recording.RecordingQuality.Nearest(VideoFramesPerSecond, Recording.RecordingQuality.VideoFramesPerSecondChoices),
            VideoMaxMinutes = Recording.RecordingQuality.Nearest(VideoMaxMinutes, Recording.RecordingQuality.VideoMaxMinutesChoices),
        };
        RecordFormat = RecordFormat?.ToLowerInvariant() == "video" ? "video" : "gif";
        RecordCountdownSeconds = CountdownChoices.MinBy(choice => Math.Abs(choice - RecordCountdownSeconds));
        RecordAudioOffsetMs = AudioOffsetChoices.MinBy(choice => Math.Abs(choice - RecordAudioOffsetMs));
        BeautifyStyle = new Output.BeautifyStyle(
            Output.BeautifyBackground.Find(BeautifyBackground).Key,
            Output.BeautifyStyle.Nearest(BeautifyPadding, Output.BeautifyStyle.PaddingChoices),
            Output.BeautifyStyle.Nearest(BeautifyRadius, Output.BeautifyStyle.RadiusChoices),
            BeautifyShadow);
    }
}
