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
    }
}
