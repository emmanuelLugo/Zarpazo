namespace Zarpazo.Recording;

/// <summary>
/// The rules of the list of presets, without windows: names are unique (whatever the case), saving under a name already
/// there replaces that preset, and a list read from a hand-edited file is tidied into one that follows the rules.
/// Every change returns a new list; null when it is refused.
/// </summary>
internal static class VideoPresetList
{
    /// <summary>More than this and the oldest beyond it are dropped from a file; the window never makes so many.</summary>
    public const int MaxCount = 50;

    public static VideoPreset? Find(IReadOnlyList<VideoPreset> presets, string? name) =>
        presets.FirstOrDefault(preset => string.Equals(preset.Name, VideoPreset.TidyName(name), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>The list with <paramref name="look"/> saved as <paramref name="name"/>: in place of a preset of that name, or at the end.</summary>
    public static IReadOnlyList<VideoPreset>? Save(IReadOnlyList<VideoPreset> presets, string? name, VideoLook look)
    {
        if (VideoPreset.TidyName(name) is not { } tidy)
        {
            return null;
        }

        var preset = new VideoPreset(tidy, look.Tidied());
        var list = presets.ToList();
        int at = list.FindIndex(other => string.Equals(other.Name, tidy, StringComparison.CurrentCultureIgnoreCase));
        if (at >= 0)
        {
            list[at] = preset with { Name = list[at].Name }; // the name keeps the letters it had; renaming changes them
        }
        else if (list.Count < MaxCount)
        {
            list.Add(preset);
        }
        else
        {
            return null;
        }
        return list;
    }

    /// <summary>The list with a preset renamed; refused when the new name is empty or another preset has it.</summary>
    public static IReadOnlyList<VideoPreset>? Rename(IReadOnlyList<VideoPreset> presets, string? name, string? newName)
    {
        VideoPreset? preset = Find(presets, name);
        string? tidy = VideoPreset.TidyName(newName);
        if (preset == null || tidy == null)
        {
            return null;
        }
        if (Find(presets, tidy) is { } other && !ReferenceEquals(other, preset))
        {
            return null;
        }
        return presets.Select(item => ReferenceEquals(item, preset) ? item with { Name = tidy } : item).ToList();
    }

    public static IReadOnlyList<VideoPreset>? Delete(IReadOnlyList<VideoPreset> presets, string? name) =>
        Find(presets, name) is { } preset ? presets.Where(item => !ReferenceEquals(item, preset)).ToList() : null;

    /// <summary>A list read from the settings file, made to follow the rules: no empty entries or names, no repeated names, values in range.</summary>
    public static IReadOnlyList<VideoPreset> Tidy(IEnumerable<VideoPreset?>? presets)
    {
        var list = new List<VideoPreset>();
        foreach (VideoPreset? preset in presets ?? [])
        {
            if (preset != null && VideoPreset.TidyName(preset.Name) is { } name && Find(list, name) == null && list.Count < MaxCount)
            {
                list.Add(new VideoPreset(name, (preset.Look ?? VideoLook.Default).Tidied()));
            }
        }
        return list;
    }
}
