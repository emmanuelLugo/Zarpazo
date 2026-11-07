using System.Text.Json.Serialization;
using Zarpazo.Shell;

namespace Zarpazo.Settings;

/// <summary>What the person can change. Plain values, so the file is easy to read and to edit by hand.</summary>
internal sealed class AppSettings
{
    public const string DefaultColor = "#E53935";
    public const double DefaultThickness = 4;
    public const int DefaultHistoryLimit = 100;

    public uint HotKeyModifiers { get; set; } = HotKeyBinding.Default.Modifiers;
    public uint HotKeyVirtualKey { get; set; } = HotKeyBinding.Default.VirtualKey;
    public bool CopyAsFile { get; set; } = true;

    /// <summary>Where the Save dialog opens. Empty means the Pictures folder.</summary>
    public string SaveFolder { get; set; } = "";

    /// <summary>"png" or "jpg".</summary>
    public string SaveFormat { get; set; } = "png";

    /// <summary>Keep every copied or saved capture in the history.</summary>
    public bool KeepHistory { get; set; } = true;

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

    /// <summary>The microphone chosen for videos; empty means the one Windows uses by default.</summary>
    public string RecordMicrophoneId { get; set; } = "";

    [JsonIgnore]
    public Recording.RecordingOptions RecordingOptions
    {
        get => new(RecordFormat == "video" ? Recording.RecordingFormat.Video : Recording.RecordingFormat.Gif, RecordMicrophone, RecordSystemSound, RecordMicrophoneId ?? "");
        set
        {
            RecordFormat = value.Format == Recording.RecordingFormat.Video ? "video" : "gif";
            RecordMicrophone = value.Microphone;
            RecordSystemSound = value.SystemSound;
            RecordMicrophoneId = value.MicrophoneId;
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
        SaveFormat = SaveFormat?.ToLowerInvariant() == "jpg" ? "jpg" : "png";
        SaveFolder ??= "";
        if (HistoryLimit < 1)
        {
            HistoryLimit = DefaultHistoryLimit;
        }
        Color ??= DefaultColor;
        RecordFormat = RecordFormat?.ToLowerInvariant() == "video" ? "video" : "gif";
        BeautifyStyle = new Output.BeautifyStyle(
            Output.BeautifyBackground.Find(BeautifyBackground).Key,
            Output.BeautifyStyle.Nearest(BeautifyPadding, Output.BeautifyStyle.PaddingChoices),
            Output.BeautifyStyle.Nearest(BeautifyRadius, Output.BeautifyStyle.RadiusChoices),
            BeautifyShadow);
    }
}
